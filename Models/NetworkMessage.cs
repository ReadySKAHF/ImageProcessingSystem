using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Newtonsoft.Json;

namespace ImageProcessingSystem.Models
{
    /// <summary>
    /// Базовое сетевое сообщение для UDP коммуникации
    /// </summary>
    [Serializable]
    public class NetworkMessage
    {
        /// <summary>
        /// Тип сообщения
        /// </summary>
        public MessageType Type { get; set; }

        /// <summary>
        /// Полезная нагрузка (данные)
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Уникальный идентификатор сообщения
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// IP адрес отправителя
        /// </summary>
        public string SenderIp { get; set; }

        /// <summary>
        /// Порт отправителя
        /// </summary>
        public int SenderPort { get; set; }

        /// <summary>
        /// Временная метка создания сообщения
        /// </summary>
        public DateTime Timestamp { get; set; }

        public NetworkMessage()
        {
            MessageId = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// Сериализация сообщения в байты для передачи по UDP
        /// </summary>
        public byte[] Serialize()
        {
            try
            {
                string json = JsonConvert.SerializeObject(this);
                return Encoding.UTF8.GetBytes(json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сериализации сообщения: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Десериализация сообщения из байтов
        /// </summary>
        public static NetworkMessage Deserialize(byte[] data)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                return JsonConvert.DeserializeObject<NetworkMessage>(json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка десериализации сообщения: {ex.Message}", ex);
            }
        }
    }
}