﻿using Lime.Protocol.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lime.Protocol.Contents
{
    public partial class TextContent
    {
        /// <summary>
        /// Writes the json to the
        /// specified writer.
        /// </summary>
        /// <param name="writer">The writer.</param>
        public override void WriteJson(IJsonWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException("writer");
            }

            writer.WriteStringProperty(TEXT_KEY, this.Text);
        }

        /// <summary>
        /// Creates an instance of the
        /// type using the passed JsonObject.
        /// </summary>
        /// <param name="jsonObject"></param>
        /// <returns></returns>
        [Factory]
        public static Document FromJsonObject(JsonObject jsonObject)
        {
            if (jsonObject == null)
            {
                throw new ArgumentNullException("jsonObject");
            }
        
            var document = new TextContent();
            document.Text = jsonObject.GetValueOrDefault(TEXT_KEY, v => (string)v);
            return document;
        }
    }
}