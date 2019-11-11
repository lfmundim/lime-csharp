﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Lime.Protocol
{
    /// <summary>
    /// Transports information about events associated to a message in a session. 
    /// Can be originated by a server or by the message destination node.
    /// </summary>
    [DataContract(Namespace = "http://limeprotocol.org/2014")]
    public class Notification : Envelope
    {
        public const string EVENT_KEY = "event";
        public const string REASON_KEY = "reason";

        public Notification()
            : base(null)
        {

        }

        public Notification(string id)
            : base(id)
        {

        }

        /// <summary>
        /// Related event to the notification
        /// </summary>
        [DataMember(Name = EVENT_KEY)]
        public Event Event { get; set; }

        /// <summary>
        /// In the case of a failed event, 
        /// brings more details about 
        /// the problem.
        /// </summary>
        [DataMember(Name = REASON_KEY)]
        public Reason Reason { get; set; }
    }

    /// <summary>
    /// Events that can happen in the message pipeline.
    /// </summary>
    [DataContract(Namespace = "http://limeprotocol.org/2014")]
    public enum Event
    {
        /// <summary>
        /// A problem occurred during the processing of the message. 
        /// In this case, the reason  property of the notification SHOULD be present.
        /// </summary>
        [EnumMember(Value = "failed")]
        Failed,

        /// <summary>
        /// The message was received and accepted by the server.
        /// This event is similar to the <see cref="Received"/> but is emitted by an intermediate node (hop) and not by the message's final destination.
        /// </summary>
        [EnumMember(Value = "accepted")]
        Accepted,

        /// <summary>
        /// The message format was validated by the server.
        /// </summary>
        [EnumMember(Value = "validated")]
        [Obsolete("This specific event should not be sent anymore")]
        Validated,

        /// <summary>
        /// The dispatch of the message was authorized by the server.
        /// </summary>
        [EnumMember(Value = "authorized")]
        [Obsolete("This specific event should not be sent anymore")]
        Authorized,

        /// <summary>
        /// The message was dispatched to the destination by the server.
        /// This event is similar to the <see cref="Consumed"/> but is emitted by an intermediate node (hop) and not by the message's final destination.
        /// </summary>
        [EnumMember(Value = "dispatched")]
        Dispatched,

        /// <summary>
        /// The node has received the message.
        /// </summary>
        [EnumMember(Value = "received")]        
        Received,

        /// <summary>
        /// The node has consumed the content of the message.
        /// </summary>
        [EnumMember(Value = "consumed")]
        Consumed
    }
}
