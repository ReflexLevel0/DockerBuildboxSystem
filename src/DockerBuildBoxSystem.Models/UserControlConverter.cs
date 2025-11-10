using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Models
{
    /// <summary>
    /// Provides custom JSON serialization and deserialization logic for <see cref="UserControlDefinition"/> objects.
    /// </summary>
    /// <remarks>This converter supports polymorphic deserialization of different control types, such as
    /// buttons, dropdowns, and textboxes, based on the "Control" property in the JSON data. During serialization, the
    /// appropriate type information is preserved.</remarks>
    public class UserControlConverter: JsonConverter<UserControlDefinition>
    {
        /// <summary>
        /// Reads and converts JSON data into a <see cref="UserControlDefinition"/> object.
        /// </summary>
        /// <param name="reader">The <see cref="Utf8JsonReader"/> to read the JSON data from.</param>
        /// <param name="typeToConvert">The type of the object to convert to. This parameter is ignored in this implementation.</param>
        /// <param name="options">The <see cref="JsonSerializerOptions"/> to use during deserialization.</param>
        /// <returns>A <see cref="UserControlDefinition"/> object representing the deserialized control,  or <see
        /// langword="null"/> if the JSON data is invalid.</returns>
        /// <exception cref="JsonException">Thrown if the JSON data is missing the required "Control" property or if the control type is unknown.</exception>
        public override UserControlDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var jsonDoc = JsonDocument.ParseValue(ref reader);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("Control", out var controlTypeProperty))
            {
                throw new JsonException("Missing 'Control' property.");
            }

            var controlType = controlTypeProperty.GetString()?.ToLowerInvariant();
            return controlType switch
            {
                "button" => JsonSerializer.Deserialize<ButtonCommand>(root.GetRawText(), options),
                "dropdown" => JsonSerializer.Deserialize<DropdownOption>(root.GetRawText(), options),
                "textbox" => JsonSerializer.Deserialize<TextBoxCommand>(root.GetRawText(), options),
                _ => throw new JsonException($"Unknown control type: {controlType}"),
            };
        }

        /// <summary>
        /// Writes the specified <see cref="UserControlDefinition"/> object to the provided <see cref="Utf8JsonWriter"/>
        /// instance using the specified <see cref="JsonSerializerOptions"/>.
        /// </summary>
        /// <param name="writer">The <see cref="Utf8JsonWriter"/> to which the <see cref="UserControlDefinition"/> object will be written.
        /// Cannot be <c>null</c>.</param>
        /// <param name="value">The <see cref="UserControlDefinition"/> object to serialize. Cannot be <c>null</c>.</param>
        /// <param name="options">The <see cref="JsonSerializerOptions"/> to use during serialization. Cannot be <c>null</c>.</param>
        public override void Write(Utf8JsonWriter writer, UserControlDefinition value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, (object)value, value.GetType(), options);
        }
    }
}
