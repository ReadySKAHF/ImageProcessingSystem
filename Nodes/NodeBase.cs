using System;
using ImageProcessingSystem.Services;

namespace ImageProcessingSystem.Nodes
{
    /// <summary>
    /// Базовый класс для всех узлов системы
    /// </summary>
    public abstract class NodeBase : INode, IDisposable
    {
        protected UdpService _udpService;
        protected bool _isRunning;

        public bool IsRunning => _isRunning;

        public event EventHandler<LogEventArgs> LogMessage;

        protected NodeBase(int port)
        {
            _udpService = new UdpService(port);
            _udpService.MessageReceived += OnMessageReceived;
            _udpService.ErrorOccurred += OnUdpError;
        }

        public virtual void Start()
        {
            if (_isRunning)
            {
                Log("Узел уже запущен", LogLevel.Warning);
                return;
            }

            try
            {
                _udpService.StartListening();
                _isRunning = true;
                Log($"Узел запущен на порту {_udpService.Port}", LogLevel.Success);
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска узла: {ex.Message}", LogLevel.Error);
            }
        }

        public virtual void Stop()
        {
            if (!_isRunning)
            {
                Log("Узел уже остановлен", LogLevel.Warning);
                return;
            }

            try
            {
                _udpService.StopListening();
                _isRunning = false;
                Log("Узел остановлен", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"Ошибка остановки узла: {ex.Message}", LogLevel.Error);
            }
        }

        protected abstract void OnMessageReceived(object sender, MessageReceivedEventArgs e);

        protected virtual void OnUdpError(object sender, Services.ErrorEventArgs e)
        {
            Log($"UDP ошибка: {e.ErrorMessage}", LogLevel.Error);
        }

        protected void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(this, new LogEventArgs(message, level));
        }

        public virtual void Dispose()
        {
            Stop();
            _udpService?.Dispose();
        }
    }
}