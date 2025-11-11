using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ImageProcessingSystem.Models;

namespace ImageProcessingSystem.Services
{
    /// <summary>
    /// Сервис для работы с UDP протоколом
    /// </summary>
    public class UdpService : IDisposable
    {
        private UdpClient _udpClient;
        private bool _isListening;
        private CancellationTokenSource _cancellationTokenSource;

        public int Port { get; private set; }
        public bool IsListening => _isListening;

        /// <summary>
        /// Событие получения сообщения
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// Событие возникновения ошибки
        /// </summary>
        public event EventHandler<ErrorEventArgs> ErrorOccurred;

        public UdpService(int port)
        {
            Port = port;
        }

        /// <summary>
        /// Запуск прослушивания UDP порта
        /// </summary>
        public void StartListening()
        {
            if (_isListening)
                return;

            try
            {
                _udpClient = new UdpClient(Port);
                _cancellationTokenSource = new CancellationTokenSource();
                _isListening = true;

                Task.Run(() => ListenAsync(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                OnError($"Ошибка запуска прослушивания на порту {Port}: {ex.Message}");
            }
        }

        /// <summary>
        /// Остановка прослушивания
        /// </summary>
        public void StopListening()
        {
            if (!_isListening)
                return;

            _isListening = false;
            _cancellationTokenSource?.Cancel();
            _udpClient?.Close();
        }

        /// <summary>
        /// Асинхронное прослушивание UDP порта
        /// </summary>
        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (_isListening && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync();

                    NetworkMessage message = NetworkMessage.Deserialize(result.Buffer);
                    message.SenderIp = result.RemoteEndPoint.Address.ToString();
                    message.SenderPort = result.RemoteEndPoint.Port;

                    OnMessageReceived(message, result.RemoteEndPoint);
                }
                catch (ObjectDisposedException)
                {
                    // UDP клиент закрыт, выходим из цикла
                    break;
                }
                catch (Exception ex)
                {
                    if (_isListening)
                    {
                        OnError($"Ошибка получения сообщения: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Отправка сообщения
        /// </summary>
        public async Task<bool> SendMessageAsync(NetworkMessage message, string targetIp, int targetPort)
        {
            try
            {
                byte[] data = message.Serialize();

                // UDP имеет ограничение на размер пакета - обычно 65507 байт
                const int MAX_UDP_SIZE = 60000; // Безопасный лимит с запасом

                if (data.Length > MAX_UDP_SIZE)
                {
                    OnError($"Размер пакета {data.Length} байт превышает безопасный лимит UDP ({MAX_UDP_SIZE} байт)!");
                    // Всё равно пытаемся отправить
                }

                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(targetIp), targetPort);

                // Используем тот же UdpClient который слушает, чтобы отправлять с правильного порта
                if (_udpClient != null && _isListening)
                {
                    await _udpClient.SendAsync(data, data.Length, endPoint);
                }
                else
                {
                    // Если не слушаем, создаём временный клиент
                    using (UdpClient client = new UdpClient())
                    {
                        await client.SendAsync(data, data.Length, endPoint);
                    }
                }

                // Небольшая задержка после отправки больших пакетов
                if (data.Length > 10000)
                {
                    await Task.Delay(50); // 50ms задержка для больших пакетов
                }

                return true;
            }
            catch (Exception ex)
            {
                OnError($"Ошибка отправки сообщения на {targetIp}:{targetPort}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Синхронная отправка сообщения
        /// </summary>
        public bool SendMessage(NetworkMessage message, string targetIp, int targetPort)
        {
            return SendMessageAsync(message, targetIp, targetPort).Result;
        }

        private void OnMessageReceived(NetworkMessage message, IPEndPoint sender)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs
            {
                Message = message,
                Sender = sender
            });
        }

        private void OnError(string errorMessage)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                ErrorMessage = errorMessage,
                Timestamp = DateTime.Now
            });
        }

        public void Dispose()
        {
            StopListening();
            _udpClient?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Аргументы события получения сообщения
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        public NetworkMessage Message { get; set; }
        public IPEndPoint Sender { get; set; }
    }

    /// <summary>
    /// Аргументы события ошибки
    /// </summary>
    public class ErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }
}