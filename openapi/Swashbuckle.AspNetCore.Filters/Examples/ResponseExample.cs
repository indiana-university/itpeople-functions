using Swashbuckle.AspNetCore.Swagger;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace Swashbuckle.AspNetCore.Filters
{
    internal class ResponseExample
    {
        private readonly JsonFormatter jsonFormatter;
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly SerializerSettingsDuplicator serializerSettingsDuplicator;

        public ResponseExample(
            JsonFormatter jsonFormatter,
            JsonSerializerSettings jsonSerializerSettings,
            SerializerSettingsDuplicator serializerSettingsDuplicator)
        {
            this.jsonFormatter = jsonFormatter;
            this.jsonSerializerSettings = jsonSerializerSettings;
            this.serializerSettingsDuplicator = serializerSettingsDuplicator;
        }

        public void SetResponseExampleForStatusCode(
            Operation operation,
            int statusCode,
            object example,
            IContractResolver contractResolver = null,
            JsonConverter jsonConverter = null)
        {
            if (example == null)
            {
                return;
            }

            var response = operation.Responses.FirstOrDefault(r => r.Key == statusCode.ToString());

            if (response.Equals(default(KeyValuePair<string, Response>)) == false && response.Value != null)
            {
                var serializerSettings = jsonSerializerSettings ?? serializerSettingsDuplicator.SerializerSettings(contractResolver, jsonConverter);
                response.Value.Examples = jsonFormatter.FormatJson(example, serializerSettings, includeMediaType: true);
            }
        }
    }
}
