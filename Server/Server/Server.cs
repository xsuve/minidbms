﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Server {
    public class TableAttribute {
        string name;
        string type;
        int length;
        bool isNull = false;
        bool isUnique = false;

        public TableAttribute(string name, string type, int length, bool isNull, bool isUnique) {
            this.name = name;
            this.type = type;
            this.length = length;
            this.isNull = isNull;
            this.isUnique = isUnique;
        }
    }

    public class Program {
        public static Socket server;
        public static XmlDocument catalog = new XmlDocument();

        public static string currentDatabase;

        public static void Main(string[] args) {
            IPAddress ipAddress = Dns.GetHostEntry("localhost").AddressList[0];
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, 11000);

            try {
                Socket listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(ipEndPoint);
                listener.Listen(100);

                Console.WriteLine("Se asteapta o conexiune...");
                server = listener.Accept();

                clientList();

                while (true) {
                    Message fromClient = receive();
                    if (fromClient != null) {
                        if (fromClient.action != MessageAction.ERROR) {
                            interpretResponse(fromClient);
                        } else {
                            error(fromClient.value);
                        }
                    }
                }
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }
        public static void interpretResponse(Message response) {
            switch (response.action) {
                case MessageAction.SQL_QUERY_REQUEST:
                    SQLQuery sqlQuery = parseStatement(response.value);
                    if (sqlQuery != null && sqlQuery.error == null) {
                        Console.WriteLine("Executa query: {0}", response.value);
                        executeQuery(sqlQuery);
                    } else {
                        send(new Message(MessageAction.ERROR, sqlQuery.error));
                    }
                    break;

                case MessageAction.CLOSE_CONNECTION:
                default:
                    server.Shutdown(SocketShutdown.Both);
                    server.Close();

                    Environment.Exit(0);
                    break;
            }
        }

        public static XmlNode createXmlNodeWithAttributes(string nodeName, Dictionary<string, string> nodeAttributes, string nodeValue = null) {
            XmlNode node = catalog.CreateElement(nodeName);
            node.InnerText = nodeValue;
            XmlAttribute attribute;
            foreach (var item in nodeAttributes) {
                attribute = catalog.CreateAttribute(item.Key);
                attribute.Value = item.Value;
                node.Attributes.Append(attribute);
            }

            return node;
        }

        public static void appendXmlNodeTo(XmlNode node, string parentNamePath) {
            catalog.Load("../../../Catalog.xml");

            XmlNode parentNode = catalog.SelectSingleNode(parentNamePath);
            if (parentNode != null) {
                parentNode.AppendChild(node);
            }

            catalog.Save("../../../Catalog.xml");
        }

        public static bool xmlNodeExists(string nodeNamePath) {
            catalog.Load("../../../Catalog.xml");

            XmlNode node = catalog.SelectSingleNode(nodeNamePath);
            return node != null;
        }

        public static void removeXmlNodeFrom(string nodeNamePath, string parentNamePath) {
            catalog.Load("../../../Catalog.xml");

            XmlNode parentNode = catalog.SelectSingleNode(parentNamePath);
            if (parentNode != null) {
                XmlNode node = catalog.SelectSingleNode(parentNamePath + "/" + nodeNamePath);
                if (node != null) {
                    parentNode.RemoveChild(node);
                }
            }

            catalog.Save("../../../Catalog.xml");
        }

        public static void executeQuery(SQLQuery sqlQuery) {
            catalog.Load("../../../Catalog.xml");

            XmlNode databasesNode = catalog.SelectSingleNode(@"//Databases");
            if (databasesNode != null) {
                switch (sqlQuery.type) {
                    case SQLQueryType.CREATE_DATABASE:
                        if (xmlNodeExists(@"//Databases/Database[@databaseName='" + sqlQuery.CREATE_DATABASE_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Baza de date '" + sqlQuery.CREATE_DATABASE_NAME + "' exista deja."));
                            return;
                        }

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("Database", new Dictionary<string, string> {
                                { "databaseName", sqlQuery.CREATE_DATABASE_NAME }
                            }),
                            @"//Databases"
                        );

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("Tables", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName='" + sqlQuery.CREATE_DATABASE_NAME + "']"
                        );

                        currentDatabase = sqlQuery.CREATE_DATABASE_NAME;

                        send(new Message(MessageAction.SUCCESS, "Baza de date '" + sqlQuery.CREATE_DATABASE_NAME + "' creata cu succes!"));
                        break;

                    case SQLQueryType.CREATE_TABLE:
                        if (currentDatabase == null) {
                            send(new Message(MessageAction.ERROR, "Selectati o baza de date inainte."));
                            return;
                        }

                        if (xmlNodeExists(@"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_TABLE_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Tabela '" + sqlQuery.CREATE_TABLE_NAME + "' exista deja."));
                            return;
                        }

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("Table", new Dictionary<string, string> {
                                { "tableName", sqlQuery.CREATE_TABLE_NAME },
                                { "fileName", sqlQuery.CREATE_TABLE_NAME + ".b" }
                            }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables"
                        );

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("Structure", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_TABLE_NAME + "']"
                        );
                        foreach (var item in sqlQuery.CREATE_TABLE_ATTRIBUTES) {
                            appendXmlNodeTo(
                                createXmlNodeWithAttributes("Attribute", new Dictionary<string, string> {
                                    { "name", item.Key },
                                    { "type", item.Value },
                                    { "isnull", "false" }
                                }),
                                @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_TABLE_NAME + "']/Structure"
                            );
                        }

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("PrimaryKey", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_TABLE_NAME + "']"
                        );
                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("UniqueKeys", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_TABLE_NAME + "']"
                        );
                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("IndexFiles", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_TABLE_NAME + "']"
                        );

                        // TODO: Create .kv file for table. create table students (id int, name varchar, tel int);
                        // Key: 1
                        // Value: 'Rusu#07516233'

                        send(new Message(MessageAction.SUCCESS, "Tabela '" + sqlQuery.CREATE_TABLE_NAME + "' creata cu succes!"));
                        break;

                    case SQLQueryType.CREATE_INDEX:
                        if (!xmlNodeExists(@"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_INDEX_TABLE_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Tabela '" + sqlQuery.CREATE_TABLE_NAME + "' nu exista."));
                            return;
                        }

                        if (xmlNodeExists(@"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_INDEX_TABLE_NAME + "']/IndexFiles/IndexFile[@indexName='" + sqlQuery.CREATE_INDEX_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Indexul '" + sqlQuery.CREATE_INDEX_NAME + "' exista deja."));
                            return;
                        }

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("IndexFile", new Dictionary<string, string> {
                                { "indexFileName", sqlQuery.CREATE_INDEX_NAME + ".b" }
                            }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_INDEX_TABLE_NAME + "']/IndexFiles"
                        );

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("IndexAttributes", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_INDEX_TABLE_NAME + "']/IndexFiles/IndexFile[@indexFileName='" + sqlQuery.CREATE_INDEX_NAME + ".b" + "']"
                        );

                        appendXmlNodeTo(
                            createXmlNodeWithAttributes("IndexAttribute", new Dictionary<string, string> { }),
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.CREATE_INDEX_TABLE_NAME + "']/IndexFiles/IndexFile[@indexFileName='" + sqlQuery.CREATE_INDEX_NAME + ".b" + "']/IndexAttributes"
                        );

                        send(new Message(MessageAction.SUCCESS, "Index '" + sqlQuery.CREATE_INDEX_NAME + "' creat cu succes!"));
                        break;

                    case SQLQueryType.DROP_DATABASE:
                        if (!xmlNodeExists(@"//Databases/Database[@databaseName='" + sqlQuery.DROP_DATABASE_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Baza de date '" + sqlQuery.DROP_DATABASE_NAME + "' nu exista."));
                            return;
                        }

                        removeXmlNodeFrom(
                            @"Database[@databaseName='" + sqlQuery.DROP_DATABASE_NAME + "']",
                            @"//Databases"
                        );

                        send(new Message(MessageAction.SUCCESS, "Baza de date '" + sqlQuery.DROP_DATABASE_NAME + "' stearsa cu succes!"));
                        break;

                    case SQLQueryType.DROP_TABLE:
                        if (!xmlNodeExists(@"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables/Table[@tableName='" + sqlQuery.DROP_TABLE_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Tabela '" + sqlQuery.DROP_TABLE_NAME + "' nu exista."));
                            return;
                        }

                        removeXmlNodeFrom(
                            @"Table[@tableName='" + sqlQuery.DROP_TABLE_NAME + "']",
                            @"//Databases/Database[@databaseName = '" + currentDatabase + "']/Tables"
                        );

                        send(new Message(MessageAction.SUCCESS, "Tabela '" + sqlQuery.DROP_TABLE_NAME + "' stearsa cu succes!"));
                        break;

                    case SQLQueryType.USE_DATABASE:
                        if (!xmlNodeExists(@"//Databases/Database[@databaseName='" + sqlQuery.CREATE_DATABASE_NAME + "']")) {
                            send(new Message(MessageAction.ERROR, "Baza de date '" + sqlQuery.CREATE_DATABASE_NAME + "' nu exista."));
                            return;
                        }

                        currentDatabase = sqlQuery.USE_DATABASE_NAME;
                        break;

                    default:
                        break;
                }
            } else {
                send(new Message(MessageAction.ERROR, "Nu exista 'Databases' in 'Catalog.xml'."));
            }

            catalog.Save("../../../Catalog.xml");
        }

        public static SQLQuery parseStatement(string statement) {
            statement = statement.Replace(";", String.Empty);
            string pattern = @"\(\s?(.+?\)?)\)";

            List<string> matches = Regex.Matches(statement, pattern).Cast<Match>().Select(match => match.Value).ToList();
            string replacedStatement = Regex.Replace(statement, pattern, "%");

            string[] args = replacedStatement.Split(" ");

            string replaced;
            SQLQuery sqlQuery;
            switch (args[0].ToLower()) {
                case "create":
                    switch (args[1].ToLower()) {
                        case "database": // CREATE DATABASE db;
                            sqlQuery = new SQLQuery(SQLQueryType.CREATE_DATABASE);
                            sqlQuery.CREATE_DATABASE_NAME = args[2];
                            break;

                        case "table": // CREATE TABLE students (studID INT, groupID INT, name VARCHAR, tel INT, email VARCHAR, PRIMARY KEY (studID), FOREIGN KEY (specID) REFERENCES Specializations(specID));
                            replaced = matches[0].Substring(1, matches[0].Length - 2);
                            string[] structure = replaced.Split(",", StringSplitOptions.TrimEntries);

                            List<TableAttribute> tableAttributes = new List<TableAttribute>();
                            Dictionary<KeyType, List<string>> tableKeys = new Dictionary<KeyType, List<string>>();
                            foreach (string item in structure) {
                                if (item.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase)) {
                                    Console.WriteLine(item);
                                } else if (item.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase)) {
                                    //
                                } else {
                                    string[] tuple = item.Split(" ");
                                    //tableAttributes.Add(new TableAttribute(tuple[0], tuple[1], 11));
                                }
                            }

                            sqlQuery = new SQLQuery(SQLQueryType.CREATE_TABLE);
                            sqlQuery.CREATE_TABLE_NAME = args[2];
                            //sqlQuery.CREATE_TABLE_ATTRIBUTES = tableAttributes;
                            sqlQuery.CREATE_TABLE_KEYS = tableKeys;
                            break;

                        case "index": // CREATE INDEX idx_studID ON students (studID, email);
                            replaced = matches[0].Substring(1, matches[0].Length - 2);
                            List<string> fields = replaced.Split(",", StringSplitOptions.TrimEntries).ToList();

                            sqlQuery = new SQLQuery(SQLQueryType.CREATE_INDEX);
                            sqlQuery.CREATE_INDEX_NAME = args[2];
                            sqlQuery.CREATE_INDEX_TABLE_NAME = args[4];
                            sqlQuery.CREATE_INDEX_TABLE_FIELDS = fields;
                            break;

                        default:
                            sqlQuery = new SQLQuery(SQLQueryType.ERROR);
                            sqlQuery.error = "SQL query invalid.";
                            break;
                    }
                    break;

                case "drop":
                    switch (args[1].ToLower()) {
                        case "database": // DROP DATABASE db;
                            sqlQuery = new SQLQuery(SQLQueryType.DROP_DATABASE);
                            sqlQuery.DROP_DATABASE_NAME = args[2];
                            break;

                        case "table": // DROP TABLE students;
                            sqlQuery = new SQLQuery(SQLQueryType.DROP_TABLE);
                            sqlQuery.DROP_TABLE_NAME = args[2];
                            break;

                        default:
                            sqlQuery = new SQLQuery(SQLQueryType.ERROR);
                            sqlQuery.error = "SQL query invalid.";
                            break;
                    }
                    break;

                case "use": // USE students;
                    sqlQuery = new SQLQuery(SQLQueryType.USE_DATABASE);
                    sqlQuery.USE_DATABASE_NAME = args[1];
                    break;

                default:
                    sqlQuery = new SQLQuery(SQLQueryType.ERROR);
                    sqlQuery.error = "SQL query invalid.";

                    break;
            }

            return sqlQuery;
        }

        public static Message parseReceived(string response) {
            string[] parts = response.Split("|");
            MessageAction messageAction;
            if (Enum.TryParse<MessageAction>(parts[0], out messageAction)) {
                return new Message(messageAction, parts[1]);
            } else {
                return null;
            }
        }

        public static Message receive() {
            byte[] bytes = new byte[1024];
            int received = server.Receive(bytes);
            string response = Encoding.ASCII.GetString(bytes, 0, received);

            Message message = parseReceived(response);
            return message;
        }

        public static void send(Message message) {
            byte[] _message = Encoding.ASCII.GetBytes(message.ToString());
            server.Send(_message);
        }

        public static void error(string message) {
            Console.Clear();
            Console.WriteLine("Eroare: {0}", message);
        }

        public static void clientList() {
            Console.Clear();
            Console.WriteLine("Client ({0}) conectat.", server.RemoteEndPoint.ToString());
            Console.WriteLine();
        }
    }
}