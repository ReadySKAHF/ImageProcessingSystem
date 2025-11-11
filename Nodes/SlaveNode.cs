using System;
using System.Threading.Tasks;
using ImageProcessingSystem.Models;
using ImageProcessingSystem.Services;
using Newtonsoft.Json;

namespace ImageProcessingSystem.Nodes
{
    /// <summary>
    /// Slave узел для обработки изображений
    /// </summary>
    public class SlaveNode : NodeBase
    {
        private string _masterIp;
        private int _masterPort;
        private MedianFilterService _filterService;

        public SlaveNode(int port, string masterIp, int masterPort) : base(port)
        {
            _masterIp = masterIp;
            _masterPort = masterPort;
            _filterService = new MedianFilterService();
        }

        public override void Start()
        {
            base.Start();
            RegisterWithMaster();
        }

        /// <summary>
        /// Регистрация на Master узле
        /// </summary>
        private async void RegisterWithMaster()
        {
            try
            {
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"🔗 РЕГИСТРАЦИЯ НА MASTER УЗЛЕ");
                Log($"   Master адрес: {_masterIp}:{_masterPort}");
                Log($"   Локальный порт: {_udpService.Port}");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                SlaveRegistrationData regData = new SlaveRegistrationData
                {
                    IpAddress = "127.0.0.1",
                    Port = _udpService.Port
                };

                string dataJson = JsonConvert.SerializeObject(regData);
                byte[] data = System.Text.Encoding.UTF8.GetBytes(dataJson);

                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.SlaveRegister,
                    Data = data
                };

                Log($"📤 Отправка запроса на регистрацию...");
                bool sent = await _udpService.SendMessageAsync(message, _masterIp, _masterPort);

                if (sent)
                {
                    Log($"✅ Запрос отправлен, ожидание подтверждения...");
                }
                else
                {
                    Log($"❌ Не удалось отправить запрос на регистрацию", LogLevel.Error);
                    Log($"   Проверьте что Master узел запущен на {_masterIp}:{_masterPort}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка регистрации: {ex.Message}", LogLevel.Error);
            }
        }

        protected override void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                switch (e.Message.Type)
                {
                    case MessageType.ImageRequest:
                        ProcessImageRequest(e);
                        break;

                    case MessageType.Acknowledgment:
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        Log($"✅ РЕГИСТРАЦИЯ ПОДТВЕРЖДЕНА!", LogLevel.Success);
                        Log($"   Slave узел успешно зарегистрирован на Master");
                        Log($"   Готов к приёму задач на обработку");
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки сообщения: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка запроса на обработку изображения
        /// </summary>
        private async void ProcessImageRequest(MessageReceivedEventArgs e)
        {
            try
            {
                string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   НОВАЯ ЗАДАЧА: {packet.FileName}");
                Log($"   PacketId: {packet.PacketId}");
                Log($"   Размер: {packet.ImageData.Length / 1024}KB");
                Log($"   Разрешение: {packet.Width}x{packet.Height}");
                Log($"   Фильтр: Медианный 15x15 (высокая интенсивность)");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                // Применяем медианный фильтр
                await Task.Run(() =>
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        Log($"   Начало обработки изображения: {packet.FileName}");

                        byte[] processedData = _filterService.ApplyMedianFilter(packet.ImageData);

                        TimeSpan processingTime = DateTime.Now - startTime;
                        Log($"   Фильтр применён за {processingTime.TotalSeconds:F2} сек");

                        // Проверяем размер результата
                        int originalSize = processedData.Length;
                        Log($"   Размер после фильтра: {originalSize / 1024}KB");

                        // КРИТИЧЕСКИ ВАЖНО: UDP на Windows имеет лимит ~65KB
                        // Целевой размер: 40KB для абсолютной гарантии доставки!

                        if (originalSize > 2000000) // >2MB - максимальное сжатие
                        {
                            Log($"   Очень большое изображение (>2MB), максимальное сжатие (качество 25%)");
                            processedData = _filterService.CompressImage(processedData, 25L);
                            Log($"   Сжатие 1: {processedData.Length / 1024}KB (было {originalSize / 1024}KB)");

                            if (processedData.Length > 40000)
                            {
                                Log($"   Дополнительное сжатие (качество 15%)...");
                                processedData = _filterService.CompressImage(processedData, 15L);
                                Log($"   Сжатие 2: {processedData.Length / 1024}KB");
                            }
                        }
                        else if (originalSize > 1000000) // 1-2MB
                        {
                            Log($"   Большое изображение (>1MB), очень агрессивное сжатие (качество 30%)");
                            processedData = _filterService.CompressImage(processedData, 30L);
                            Log($"   Сжатие 1: {processedData.Length / 1024}KB (было {originalSize / 1024}KB)");

                            if (processedData.Length > 40000)
                            {
                                Log($"   Дополнительное сжатие (качество 20%)...");
                                processedData = _filterService.CompressImage(processedData, 20L);
                                Log($"   Сжатие 2: {processedData.Length / 1024}KB");
                            }
                        }
                        else if (originalSize > 500000) // 500KB-1MB
                        {
                            Log($"   Изображение >500KB, агрессивное сжатие (качество 35%)");
                            processedData = _filterService.CompressImage(processedData, 35L);
                            Log($"   Сжатие 1: {processedData.Length / 1024}KB (было {originalSize / 1024}KB)");

                            if (processedData.Length > 40000)
                            {
                                Log($"   Дополнительное сжатие (качество 25%)...");
                                processedData = _filterService.CompressImage(processedData, 25L);
                                Log($"   Сжатие 2: {processedData.Length / 1024}KB");
                            }
                        }
                        else if (originalSize > 200000) // 200-500KB - сильное сжатие
                        {
                            Log($"   Изображение >200KB, сильное сжатие (качество 40%)");
                            processedData = _filterService.CompressImage(processedData, 40L);
                            Log($"   Сжатие 1: {processedData.Length / 1024}KB (было {originalSize / 1024}KB)");

                            if (processedData.Length > 40000)
                            {
                                Log($"   Дополнительное сжатие (качество 30%)...");
                                processedData = _filterService.CompressImage(processedData, 30L);
                                Log($"   Сжатие 2: {processedData.Length / 1024}KB");
                            }
                        }
                        else if (originalSize > 50000) // 50-200KB - среднее сжатие
                        {
                            Log($"   Изображение >50KB, среднее сжатие (качество 55%)");
                            processedData = _filterService.CompressImage(processedData, 55L);
                            Log($"   Сжатие: {processedData.Length / 1024}KB (было {originalSize / 1024}KB)");

                            if (processedData.Length > 40000)
                            {
                                Log($"   Дополнительное сжатие (качество 40%)...");
                                processedData = _filterService.CompressImage(processedData, 40L);
                                Log($"   Финальное: {processedData.Length / 1024}KB");
                            }
                        }
                        else
                        {
                            Log($"   Размер подходит для UDP, сжатие не требуется");
                        }

                        // КРИТИЧЕСКАЯ ФИНАЛЬНАЯ ПРОВЕРКА - гарантируем размер <40KB
                        // UDP на Windows имеет строгий лимит ~65KB, но с учётом накладных
                        // расходов JSON и заголовков, безопасный размер - 40KB
                        int attempts = 0;
                        while (processedData.Length > 40000 && attempts < 5)
                        {
                            attempts++;
                            int quality = Math.Max(10, 30 - (attempts * 5)); // 25, 20, 15, 10, 10
                            Log($"   Попытка {attempts}: Размер {processedData.Length / 1024}KB всё ещё велик, критическое сжатие ({quality}%)...");
                            processedData = _filterService.CompressImage(processedData, quality);
                            Log($"   Результат: {processedData.Length / 1024}KB");
                        }

                        if (processedData.Length > 40000)
                        {
                            Log($"   КРИТИЧЕСКАЯ ПРОБЛЕМА: После {attempts} попыток размер {processedData.Length / 1024}KB всё ещё >40KB!", LogLevel.Error);
                            Log($"   Изображение слишком сложное для UDP. Попробуйте уменьшить разрешение.", LogLevel.Error);
                        }

                        // Показываем итоговую статистику
                        double compressionRatio = ((1.0 - (double)processedData.Length / originalSize) * 100);
                        if (originalSize > 50000)
                        {
                            Log($"   Общее уменьшение размера: {compressionRatio:F1}%");
                        }

                        // Создаем пакет с обработанным изображением
                        ImagePacket responsePacket = new ImagePacket
                        {
                            ImageData = processedData,
                            FileName = packet.FileName,
                            Width = packet.Width,
                            Height = packet.Height,
                            Format = packet.Format,
                            PacketId = packet.PacketId,
                            SlavePort = _udpService.Port // Указываем порт Slave для идентификации
                        };

                        // Отправляем результат обратно Master узлу
                        SendProcessedImage(responsePacket);

                        TimeSpan totalTime = DateTime.Now - startTime;
                        Log($"   Обработка завершена: {packet.FileName}", LogLevel.Success);
                        Log($"   Общее время: {totalTime.TotalSeconds:F2} сек");
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    }
                    catch (Exception ex)
                    {
                        Log($"   Ошибка обработки изображения {packet.FileName}: {ex.Message}", LogLevel.Error);
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"   Ошибка обработки запроса: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Отправка обработанного изображения Master узлу
        /// </summary>
        private async void SendProcessedImage(ImagePacket packet)
        {
            try
            {
                string packetJson = JsonConvert.SerializeObject(packet);
                byte[] packetData = System.Text.Encoding.UTF8.GetBytes(packetJson);

                Log($"   Отправка результата Master узлу:");
                Log($"   Файл: {packet.FileName}");
                Log($"   PacketId: {packet.PacketId}");
                Log($"   Размер пакета: {packetData.Length / 1024}KB");

                if (packetData.Length > 58000) // Безопасный лимит UDP с запасом
                {
                    Log($"   ВНИМАНИЕ: Размер пакета {packetData.Length / 1024}KB близок к лимиту UDP!", LogLevel.Warning);
                    Log($"   Пакет может не дойти! UDP лимит ~64KB.", LogLevel.Warning);
                }
                else if (packetData.Length > 50000)
                {
                    Log($"   Размер пакета {packetData.Length / 1024}KB - в пределах нормы для UDP");
                }

                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.ImageResponse,
                    Data = packetData
                };

                bool sent = await _udpService.SendMessageAsync(message, _masterIp, _masterPort);

                if (sent)
                {
                    Log($"   Результат успешно отправлен Master узлу", LogLevel.Success);
                    Log($"   Адрес: {_masterIp}:{_masterPort}");
                }
                else
                {
                    Log($"   Не удалось отправить результат {packet.FileName}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Log($"   Ошибка отправки результата: {ex.Message}", LogLevel.Error);
            }
        }
    }
}