﻿using System;
using System.Runtime.Serialization;
using Lime.Protocol;

namespace Lime.Messaging.Contents
{
    /// <summary>
    /// Represents an external link to a media content.
    /// </summary>
    /// <seealso cref="Lime.Protocol.Document" />
    [DataContract(Namespace = "http://limeprotocol.org/2014")]
    public class MediaLink : Link
    {
        public const string MIME_TYPE = "application/vnd.lime.media-link+json";
        public const string TYPE_KEY = "type";
        public const string SIZE_KEY = "size";
        public const string ASPECT_RATIO_KEY = "aspectRatio";

        public static readonly MediaType MediaType = MediaType.Parse(MIME_TYPE);

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaLink"/> class.
        /// </summary>
        public MediaLink() 
            : base(MediaType)
        {
        }

        /// <summary>
        /// Gets or sets the media type of the linked media.
        /// </summary>
        /// <value>
        /// The type.
        /// </value>
        [DataMember(Name = TYPE_KEY)]
        public MediaType Type { get; set; }       
        
        /// <summary>
        /// Gets or sets the media size, in bytes.
        /// </summary> 
        /// <value>
        /// The size.
        /// </value>
        [DataMember(Name = SIZE_KEY)]
        public long? Size { get; set; }

        /// <summary>
        /// Gets or sets the media display aspect ratio.
        /// Common values are 1:1, 4:3 and 16:9.
        /// </summary>
        [DataMember(Name = ASPECT_RATIO_KEY)]
        public string AspectRatio { get; set; }
    }
}
