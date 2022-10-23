﻿using System;
namespace Client {
    public enum MessageAction {
        SQL_QUERY
    }

    public class Message {
        public MessageAction action;
        public string value;

        public Message(MessageAction action, string value) {
            this.action = action;
            this.value = value;
        }

        public override string ToString() {
            return this.action + "|" + this.value;
        }
    }
}

