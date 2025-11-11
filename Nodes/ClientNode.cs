using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageProcessingSystem.Models;
using ImageProcessingSystem.Services;
using Newtonsoft.Json;

namespace ImageProcessingSystem.Nodes
{
    /// <summary>
    /// Клиентский узел для отправки изображений
    /// </summary>
    public class ClientNode : NodeBase
    {
        private string _masterIp;
        private int _masterPort;
        private Dictionary<string, ImageInfo> _pendingImages;

        public List<ImageInfo> ProcessedImages { get; private set; }
        public event EventHandler<ImageProcessedEventArgs> ImageProcessed;

        public ClientNode(int port, string masterIp, int masterPort) : base(port)
        {
            _masterIp = masterIp;
            _masterPort = masterPort;
            _pendingImages = new Dictionary<string, ImageInfo>();
            ProcessedImages = new List<ImageInfo>();
        }

        /// <summary>
        /// Загрузка изображений из файлов
        /// </summary>
        public List<ImageInfo> LoadImages(string[] filePaths)
        {
            List<ImageInfo> images = new List<ImageInfo>();

            foreach (string filePath in filePaths)
            {
                try
                {
                    byte[] imageBytes = File.ReadAllBytes(filePath);

                    using (MemoryStream ms = new MemoryStream(imageBytes))
                    using (Bitmap bitmap = new Bitmap(ms))
                    {
                        ImageInfo info = new ImageInfo
                        {
                            FileName = Path.GetFileName(filePath),
                            OriginalData = imageBytes,
                            Width = bitmap.Width,
                            Height = bitmap.Height,
                            Format = bitmap.RawFormat.ToString()
                        };

                        images.Add(info);
                        Log($"Загружено изображение: {info.FileName} ({info.Width}x{info.Height})");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ошибка загрузки файла {filePath}: {ex.Message}", LogLevel.Error);
                }
            }

            return images;
        }

        /// <summary>
        /// Отправка изображения на Master узел
        /// </summary>
        public async Task<bool> SendImageAsync(ImageInfo imageInfo)
        {
            try
            {
                ImagePacket packet = new ImagePacket
                {
                    ImageData = imageInfo.OriginalData,
                    FileName = imageInfo.FileName,
                    Width = imageInfo.Width,
                    Height = imageInfo.Height,
                    Format = imageInfo.Format
                };

                string packetJson = JsonConvert.SerializeObject(packet);
                byte[] packetData = System.Text.Encoding.UTF8.GetBytes(packetJson);

                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.ImageRequest,
                    Data = packetData,
                    SenderIp = "127.0.0.1",
                    SenderPort = _udpService.Port
                };

                _pendingImages[packet.PacketId] = imageInfo;

                bool sent = await _udpService.SendMessageAsync(message, _masterIp, _masterPort);

                if (sent)
                {
                    Log($"Отправлено изображение {imageInfo.FileName} на Master ({_masterIp}:{_masterPort})");
                    return true;
                }
                else
                {
                    Log($"Не удалось отправить изображение {imageInfo.FileName}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки изображения: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Отправка всех изображений
        /// </summary>
        public async Task SendImagesAsync(List<ImageInfo> images)
        {
            foreach (var image in images)
            {
                await SendImageAsync(image);
                await Task.Delay(100); // Небольшая задержка между отправками
            }
        }

        protected override void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                Log($"Получено сообщение типа: {e.Message.Type}");

                if (e.Message.Type == MessageType.ImageResponse)
                {
                    string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                    ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                    Log($"Получен ответ: {packet.FileName}, PacketId: {packet.PacketId}");
                    Log($"Ожидается результатов: {_pendingImages.Count}");

                    if (_pendingImages.TryGetValue(packet.PacketId, out ImageInfo originalInfo))
                    {
                        originalInfo.ProcessedData = packet.ImageData;
                        ProcessedImages.Add(originalInfo);
                        _pendingImages.Remove(packet.PacketId);

                        Log($"Получено обработанное изображение: {packet.FileName}", LogLevel.Success);

                        ImageProcessed?.Invoke(this, new ImageProcessedEventArgs { ImageInfo = originalInfo });
                    }
                    else
                    {
                        Log($"ОШИБКА: Не найдено ожидающее изображение для PacketId: {packet.PacketId}", LogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки полученного сообщения: {ex.Message}", LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// Информация об изображении
    /// </summary>
    public class ImageInfo
    {
        public string FileName { get; set; }
        public byte[] OriginalData { get; set; }
        public byte[] ProcessedData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
    }

    /// <summary>
    /// Аргументы события обработки изображения
    /// </summary>
    public class ImageProcessedEventArgs : EventArgs
    {
        public ImageInfo ImageInfo { get; set; }
    }
}