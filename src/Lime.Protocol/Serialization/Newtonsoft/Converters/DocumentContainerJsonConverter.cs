﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Lime.Protocol.Serialization.Newtonsoft.Converters
{
    public class DocumentContainerJsonConverter : JsonConverter
    {
        private static readonly Dictionary<Type, bool> CanConvertDictionary = new Dictionary<Type, bool>();
        private static readonly object SyncRoot = new object();
        private readonly IDocumentTypeResolver _documentTypeResolver;

        public DocumentContainerJsonConverter(IDocumentTypeResolver documentTypeResolver)
        {
            _documentTypeResolver = documentTypeResolver;
        }

        public override bool CanRead => true;

        public override bool CanWrite => true;        

        public override bool CanConvert(Type objectType)
        {
            if (!CanConvertDictionary.TryGetValue(objectType, out var canConvert))
            {
                lock (SyncRoot)
                {
                    if (!CanConvertDictionary.TryGetValue(objectType, out canConvert))
                    {
                        // This implementation works with all classes that have a 'type' property among a typeof(Document) property, not only the DocumentContainer class.
                        if (objectType.GetTypeInfo().IsAbstract) return false;

                        var properties = objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        canConvert =
                            properties.Any(p => p.PropertyType == typeof (Document)) &&
                            properties.Any(
                                p =>
                                    p.Name.Equals(DocumentContainer.TYPE_KEY, StringComparison.OrdinalIgnoreCase) &&
                                    p.PropertyType == typeof (MediaType));

                        CanConvertDictionary.Add(objectType, canConvert);
                    }
                }
            }
            return canConvert;
        }
        
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            object target = null;
            if (reader.TokenType != JsonToken.Null)
            {
                // Initialize and populate the initial object
                target = CreateInstance(objectType);
                var jObject = JObject.Load(reader);
                serializer.Populate(jObject.CreateReader(), target);
                
                // Check if the 'type' property is present to the JSON
                if (jObject.TryGetValue(DocumentContainer.TYPE_KEY, out var mediaTypeJToken) &&
                    mediaTypeJToken.Type != JTokenType.Null)
                {
                    // Find the document property
                    var documentProperty =
                        objectType
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .First(p => p.PropertyType == typeof(Document));

                    var documentPropertyName = documentProperty.Name.ToCamelCase();
                    var mediaType = mediaTypeJToken.ToObject<MediaType>();

                    // Create the document instance                    
                    var document = jObject[documentPropertyName].ToDocument(mediaType, serializer, _documentTypeResolver);
                    documentProperty.SetValue(target, document);
                }
            }

            return target;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // The container should be always a JSON
            var contract = serializer.ContractResolver.ResolveContract(value.GetType()) as JsonObjectContract;
            if (contract == null) return;
            
            var shouldStartObject = writer.WriteState == WriteState.Start || writer.WriteState == WriteState.Array || writer.WriteState == WriteState.Property;
            if (shouldStartObject) writer.WriteStartObject();
            
            foreach (var property in contract.Properties.Where(p => p.ShouldSerialize == null || p.ShouldSerialize(value)))
            {                
                var propertyValue = property.ValueProvider.GetValue(value);
                if (propertyValue == null) continue;

                if (property.DefaultValueHandling == DefaultValueHandling.Ignore &&
                    propertyValue.Equals(property.DefaultValue ?? property.PropertyType.GetDefaultValue()))
                {                    
                    continue;
                }

                writer.WritePropertyName(property.PropertyName);

                if (propertyValue is Document document && !document.GetMediaType().IsJson)
                {
                    writer.WriteValue(document.ToString());
                }
                else
                {
                    serializer.Serialize(writer, propertyValue);
                }                
            }

            if (shouldStartObject) writer.WriteEndObject();
        }

        private static object CreateInstance(Type objectType)
        {
            if (objectType == typeof(Message)) return new Message();
            if (objectType == typeof(Command)) return new Command(null);
            return Activator.CreateInstance(objectType);
        }
    }
}