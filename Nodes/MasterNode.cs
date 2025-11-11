using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using ImageProcessingSystem.Models;
using ImageProcessingSystem.Services;
using Newtonsoft.Json;

namespace ImageProcessingSystem.Nodes
{
    /// <summary>
    /// Master узел для распределения задач
    /// </summary>
    public class MasterNode : NodeBase
    {
        private List<SlaveInfo> _registeredSlaves;
        private Dictionary<string, ClientRequestInfo> _pendingRequests;
        private Queue<PendingTask> _taskQueue; // Очередь задач
        private Dictionary<string, bool> _slaveBusyStatus; // Статус занятости Slave
        private Dictionary<string, int> _slaveTaskCount; // Счётчик задач на каждый Slave
        private Dictionary<string, double> _slaveTotalTime; // Общее время работы каждого Slave
        private Random _random;
        private int _roundRobinIndex = 0; // Индекс для Round-Robin балансировки
        private int _totalTasksReceived = 0; // Всего получено задач
        private int _totalTasksCompleted = 0; // Всего завершено задач
        private DateTime _firstTaskTime; // Время первой задачи
        private DateTime _lastTaskTime; // Время последней задачи

        public int RegisteredSlavesCount => _registeredSlaves.Count;

        public MasterNode(int port) : base(port)
        {
            _registeredSlaves = new List<SlaveInfo>();
            _pendingRequests = new Dictionary<string, ClientRequestInfo>();
            _taskQueue = new Queue<PendingTask>();
            _slaveBusyStatus = new Dictionary<string, bool>();
            _slaveTaskCount = new Dictionary<string, int>();
            _slaveTotalTime = new Dictionary<string, double>();
            _random = new Random();
        }

        public override void Start()
        {
            base.Start();
            Log("═══════════════════════════════════════════════════════");
            Log("                  MASTER УЗЕЛ ЗАПУЩЕН                  ");
            Log("═══════════════════════════════════════════════════════");
            Log("");
        }

        protected override void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                switch (e.Message.Type)
                {
                    case MessageType.SlaveRegister:
                        HandleSlaveRegistration(e);
                        break;

                    case MessageType.ImageRequest:
                        HandleImageRequest(e);
                        break;

                    case MessageType.ImageResponse:
                        HandleImageResponse(e);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки сообщения: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка регистрации Slave узла
        /// </summary>
        private void HandleSlaveRegistration(MessageReceivedEventArgs e)
        {
            try
            {
                string dataJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                SlaveRegistrationData regData = JsonConvert.DeserializeObject<SlaveRegistrationData>(dataJson);

                SlaveInfo slaveInfo = new SlaveInfo
                {
                    SlaveId = Guid.NewGuid().ToString(),
                    IpAddress = regData.IpAddress,
                    Port = regData.Port,
                    RegistrationTime = DateTime.Now
                };

                // Проверяем, не зарегистрирован ли уже этот slave
                var existingSlave = _registeredSlaves.FirstOrDefault(s =>
                    s.IpAddress == slaveInfo.IpAddress && s.Port == slaveInfo.Port);

                if (existingSlave == null)
                {
                    _registeredSlaves.Add(slaveInfo);

                    // Инициализируем статус и счётчики - Slave свободен
                    string slaveKey = $"{slaveInfo.IpAddress}:{slaveInfo.Port}";
                    _slaveBusyStatus[slaveKey] = false;
                    _slaveTaskCount[slaveKey] = 0; // Счётчик задач
                    _slaveTotalTime[slaveKey] = 0; // Общее время

                    Log($"═══════════════════════════════════════════════════════");
                    Log($"   Зарегистрирован SLAVE #{_registeredSlaves.Count}");
                    Log($"   Адрес: {slaveInfo.IpAddress}:{slaveInfo.Port}");
                    Log($"   Всего Slave узлов: {_registeredSlaves.Count}");
                    Log($"═══════════════════════════════════════════════════════");

                    // Пытаемся обработать задачи из очереди
                    ProcessTaskQueue();
                }
                else
                {
                    Log($"⚠️ Slave узел уже зарегистрирован: {slaveInfo.IpAddress}:{slaveInfo.Port}");
                }

                // Отправляем подтверждение
                SendAcknowledgment(slaveInfo.IpAddress, slaveInfo.Port);
            }
            catch (Exception ex)
            {
                Log($"Ошибка регистрации Slave: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка запроса на обработку изображения от клиента
        /// </summary>
        private void HandleImageRequest(MessageReceivedEventArgs e)
        {
            try
            {
                if (_registeredSlaves.Count == 0)
                {
                    Log("Нет доступных Slave узлов для обработки", LogLevel.Warning);
                    return;
                }

                string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                _totalTasksReceived++;

                // Запоминаем время первой задачи
                if (_totalTasksReceived == 1)
                {
                    _firstTaskTime = DateTime.Now;
                }

                Log($"");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   ЗАДАЧА #{_totalTasksReceived}: {packet.FileName}");
                Log($"   PacketId: {packet.PacketId}");
                Log($"   Размер: {packet.ImageData.Length / 1024}KB");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                // Показываем текущий статус Slave перед назначением
                if (_registeredSlaves.Count > 1)
                {
                    int freeCount = CountFreeSlaves();
                    Log($"  Доступно Slave: {freeCount}/{_registeredSlaves.Count}");
                }

                // Создаём информацию о клиенте
                ClientRequestInfo clientInfo = new ClientRequestInfo
                {
                    ClientIp = e.Sender.Address.ToString(),
                    ClientPort = e.Message.SenderPort,
                    RequestTime = DateTime.Now,
                    FileName = packet.FileName
                };

                // Сохраняем связь PacketId -> Client
                _pendingRequests[packet.PacketId] = clientInfo;

                // Создаём задачу
                PendingTask task = new PendingTask
                {
                    Message = new NetworkMessage
                    {
                        Type = MessageType.ImageRequest,
                        Data = e.Message.Data
                    },
                    PacketId = packet.PacketId,
                    FileName = packet.FileName,
                    ClientInfo = clientInfo
                };

                // Пытаемся найти свободный Slave
                SlaveInfo freeSlave = FindFreeSlave();

                if (freeSlave != null)
                {
                    // Есть свободный Slave - отправляем задачу сразу
                    AssignTaskToSlave(task, freeSlave);
                }
                else
                {
                    // Все Slave заняты - ставим в очередь
                    _taskQueue.Enqueue(task);
                    Log($"  Все Slave заняты! Задача #{_totalTasksReceived} в очередь (позиция: {_taskQueue.Count})", LogLevel.Warning);
                    ShowSlaveStatus(); // Показываем статус когда задача идёт в очередь
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки запроса изображения: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Найти свободный Slave узел с использованием Round-Robin балансировки
        /// </summary>
        private SlaveInfo FindFreeSlave()
        {
            if (_registeredSlaves.Count == 0)
                return null;

            // Собираем все свободные Slave
            List<SlaveInfo> freeSlaves = new List<SlaveInfo>();
            foreach (var slave in _registeredSlaves)
            {
                string slaveKey = $"{slave.IpAddress}:{slave.Port}";
                if (_slaveBusyStatus.ContainsKey(slaveKey) && !_slaveBusyStatus[slaveKey])
                {
                    freeSlaves.Add(slave);
                }
            }

            if (freeSlaves.Count == 0)
                return null;

            // Round-Robin: выбираем следующий свободный Slave по кругу
            // Начинаем с _roundRobinIndex и ищем следующий свободный
            SlaveInfo selectedSlave = null;

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                int index = (_roundRobinIndex + i) % _registeredSlaves.Count;
                var slave = _registeredSlaves[index];
                string slaveKey = $"{slave.IpAddress}:{slave.Port}";

                if (_slaveBusyStatus.ContainsKey(slaveKey) && !_slaveBusyStatus[slaveKey])
                {
                    selectedSlave = slave;
                    _roundRobinIndex = (index + 1) % _registeredSlaves.Count; // Следующий для следующего раза
                    break;
                }
            }

            if (selectedSlave != null)
            {
                Log($"🎯 Round-Robin выбор: Slave {selectedSlave.IpAddress}:{selectedSlave.Port}");
            }

            return selectedSlave;
        }

        /// <summary>
        /// Подсчёт свободных Slave
        /// </summary>
        private int CountFreeSlaves()
        {
            return _slaveBusyStatus.Count(kvp => !kvp.Value);
        }

        /// <summary>
        /// Назначить задачу на Slave
        /// </summary>
        private void AssignTaskToSlave(PendingTask task, SlaveInfo slave)
        {
            string slaveKey = $"{slave.IpAddress}:{slave.Port}";

            // Помечаем Slave как занятый
            _slaveBusyStatus[slaveKey] = true;

            // Увеличиваем счётчик задач для этого Slave
            if (!_slaveTaskCount.ContainsKey(slaveKey))
                _slaveTaskCount[slaveKey] = 0;

            _slaveTaskCount[slaveKey]++;

            // Сохраняем время начала в ClientInfo для подсчёта времени обработки
            task.ClientInfo.RequestTime = DateTime.Now;

            // Отправляем задачу
            _udpService.SendMessageAsync(task.Message, slave.IpAddress, slave.Port);

            int slaveNumber = _registeredSlaves.FindIndex(s => s.IpAddress == slave.IpAddress && s.Port == slave.Port) + 1;

            Log($"  Задача {task.FileName} → Slave #{slaveNumber} ({slave.IpAddress}:{slave.Port})");
            Log($"      Этот Slave обработал: {_slaveTaskCount[slaveKey]} задач");

            // Показываем статистику распределения
            int busyCount = _slaveBusyStatus.Count(kvp => kvp.Value);
            Log($"      Занято: {busyCount}/{_registeredSlaves.Count}, Свободно: {_registeredSlaves.Count - busyCount}");
            Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        /// <summary>
        /// Обработать очередь задач - отправить задачи на свободные Slave
        /// </summary>
        private void ProcessTaskQueue()
        {
            while (_taskQueue.Count > 0)
            {
                SlaveInfo freeSlave = FindFreeSlave();

                if (freeSlave == null)
                {
                    // Нет свободных Slave
                    Log($"Очередь: {_taskQueue.Count} задач ожидают, но нет свободных Slave", LogLevel.Warning);
                    ShowSlaveStatus();
                    break;
                }

                // Берём задачу из очереди
                PendingTask task = _taskQueue.Dequeue();
                Log($"Задача {task.FileName} извлечена из очереди (осталось в очереди: {_taskQueue.Count})");

                // Назначаем на свободный Slave
                AssignTaskToSlave(task, freeSlave);
            }
        }

        /// <summary>
        /// Показать статус всех Slave узлов
        /// </summary>
        private void ShowSlaveStatus()
        {
            Log("╔═══════════════════════════════════════════════════════╗");
            Log("║               СТАТУС ВСЕХ SLAVE УЗЛОВ                 ║");
            Log("╚═══════════════════════════════════════════════════════╝");

            if (_registeredSlaves.Count == 0)
            {
                Log("  ⚠️ Нет зарегистрированных Slave узлов!");
                return;
            }

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                var slave = _registeredSlaves[i];
                string key = $"{slave.IpAddress}:{slave.Port}";
                bool isBusy = _slaveBusyStatus.ContainsKey(key) && _slaveBusyStatus[key];
                string status = isBusy ? "🔴 ЗАНЯТ" : "🟢 СВОБОДЕН";
                int taskCount = _slaveTaskCount.ContainsKey(key) ? _slaveTaskCount[key] : 0;

                Log($"  [{i + 1}] {slave.IpAddress}:{slave.Port.ToString().PadRight(5)} - {status} (задач: {taskCount})");
            }

            int busyCount = _slaveBusyStatus.Count(kvp => kvp.Value);
            int freeCount = _slaveBusyStatus.Count(kvp => !kvp.Value);

            Log($"╔═══════════════════════════════════════════════════════╗");
            Log($"║ Всего: {_registeredSlaves.Count}  |     Занято: {busyCount}  |  🟢 Свободно: {freeCount}      ║");
            Log($"╚═══════════════════════════════════════════════════════╝");
        }

        /// <summary>
        /// Показать итоговую статистику производительности
        /// </summary>
        private void ShowFinalStatistics()
        {
            TimeSpan totalTime = _lastTaskTime - _firstTaskTime;

            Log($"");
            Log($"");
            Log($"╔═══════════════════════════════════════════════════════════════╗");
            Log($"║                     ВСЕ ЗАДАЧИ ЗАВЕРШЕНЫ!                     ║");
            Log($"╚═══════════════════════════════════════════════════════════════╝");
            Log($"");
            Log($" Итоговая статистика производительности:");
            Log($"");
            Log($"┌───────────────────────────────────────────────────────────┐");
            Log($"│ Общие показатели                                          │");
            Log($"├───────────────────────────────────────────────────────────┤");
            Log($"│ Всего задач обработано:     {_totalTasksCompleted}                            │");
            Log($"│ Количество Slave узлов:     {_registeredSlaves.Count}                            │");
            Log($"│ Общее время обработки:      {totalTime.TotalSeconds:F2} сек                 │");
            Log($"│ Среднее время на задачу:    {(totalTime.TotalSeconds / _totalTasksCompleted):F2} сек                 │");
            Log($"└───────────────────────────────────────────────────────────┘");
            Log($"");
            Log($"┌───────────────────────────────────────────────────────────┐");
            Log($"│               Распределение нагрузки по Slave             │");
            Log($"├───────────────────────────────────────────────────────────┤");

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                var slave = _registeredSlaves[i];
                string key = $"{slave.IpAddress}:{slave.Port}";
                int taskCount = _slaveTaskCount.ContainsKey(key) ? _slaveTaskCount[key] : 0;
                double totalSlaveTime = _slaveTotalTime.ContainsKey(key) ? _slaveTotalTime[key] : 0;
                double avgTime = taskCount > 0 ? totalSlaveTime / taskCount : 0;
                double percentage = _totalTasksCompleted > 0 ? (taskCount * 100.0 / _totalTasksCompleted) : 0;

                string bar = new string('█', (int)(percentage / 5)); // Масштаб: 5% = 1 символ

                Log($"│ Slave #{i + 1} ({slave.Port}):                                  │");
                Log($"│   Задач обработано: {taskCount} ({percentage:F1}%)                      │");
                Log($"│   Общее время: {totalSlaveTime:F2} сек                           │");
                Log($"│   Среднее время: {avgTime:F2} сек/задача                    │");
                Log($"│   Нагрузка: {bar}                                     │");
                Log($"├───────────────────────────────────────────────────────────┤");
            }

            Log($"└───────────────────────────────────────────────────────────┘");
            Log($"");

            // Вычисляем коэффициент ускорения
            if (_registeredSlaves.Count > 1)
            {
                double theoreticalTimeFor1Slave = totalTime.TotalSeconds * _registeredSlaves.Count;
                double speedup = theoreticalTimeFor1Slave / totalTime.TotalSeconds;
                double efficiency = (speedup / _registeredSlaves.Count) * 100;

                Log($"┌───────────────────────────────────────────────────────────┐");
                Log($"│                         Эффективность                     │");
                Log($"├───────────────────────────────────────────────────────────┤");
                Log($"│ Коэффициент ускорения: {speedup:F2}x                            │");
                Log($"│ Эффективность: {efficiency:F1}%                                  │");
                Log($"│                                                           │");

                if (efficiency > 80)
                {
                    Log($"│ Выше 80% эффективность.                               │");
                }
                else if (efficiency > 60)
                {
                    Log($"│ 60-80% эффективность.                                 │");
                }
                else
                {
                    Log($"│ Малая эффективность.                                  │");
                }

                Log($"└───────────────────────────────────────────────────────────┘");
                Log($"");

                // Демонстрация выигрыша от параллелизма
                Log($"┌───────────────────────────────────────────────────────────┐");
                Log($"│             ДЕМОНСТРАЦИЯ ЭФФЕКТА ПАРАЛЛЕЛИЗМА             │");
                Log($"├───────────────────────────────────────────────────────────┤");
                Log($"│ Если бы был только 1 Slave:                               │");
                Log($"│   Время обработки: ~{theoreticalTimeFor1Slave:F0} сек                        │");
                Log($"│                                                           │");
                Log($"│ С {_registeredSlaves.Count} Slave узлами:                                       │");
                Log($"│   Время обработки: {totalTime.TotalSeconds:F0} сек                              │");
                Log($"│                                                           │");
                Log($"│ ⚡ ВЫИГРЫШ: В {speedup:F1} раза быстрее! ⚡                       │");
                Log($"└───────────────────────────────────────────────────────────┘");
            }
        }

        /// <summary>
        /// Обработка ответа от Slave узла
        /// </summary>
        private void HandleImageResponse(MessageReceivedEventArgs e)
        {
            try
            {
                string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                // ВАЖНО: Используем SlavePort из пакета, а не e.Sender.Port (который случайный)!
                string slaveKey = $"{e.Sender.Address}:{packet.SlavePort}";

                _totalTasksCompleted++;
                _lastTaskTime = DateTime.Now;

                Log($"");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   РЕЗУЛЬТАТ от Slave: {packet.FileName}");
                Log($"   Размер: {e.Message.Data.Length / 1024}KB");

                // Подсчитываем время обработки
                if (_pendingRequests.TryGetValue(packet.PacketId, out ClientRequestInfo clientInfo))
                {
                    TimeSpan processingTime = DateTime.Now - clientInfo.RequestTime;

                    // Сохраняем общее время работы Slave
                    if (_slaveTotalTime.ContainsKey(slaveKey))
                    {
                        _slaveTotalTime[slaveKey] += processingTime.TotalSeconds;
                    }

                    int slaveNumber = _registeredSlaves.FindIndex(s =>
                        $"{s.IpAddress}:{s.Port}" == slaveKey) + 1;

                    Log($"      Время обработки: {processingTime.TotalSeconds:F2} сек");
                    Log($"      Обработал: Slave #{slaveNumber}");
                }

                if (_slaveBusyStatus.ContainsKey(slaveKey))
                {
                    _slaveBusyStatus[slaveKey] = false;
                    Log($"   Slave {slaveKey} теперь СВОБОДЕН!");
                }
                else
                {
                    Log($"   Slave {slaveKey} не найден в списке!", LogLevel.Warning);
                }

                if (_pendingRequests.ContainsKey(packet.PacketId))
                {
                    // Пересылаем результат клиенту
                    NetworkMessage clientMessage = new NetworkMessage
                    {
                        Type = MessageType.ImageResponse,
                        Data = e.Message.Data
                    };

                    _udpService.SendMessageAsync(clientMessage, clientInfo.ClientIp, clientInfo.ClientPort);

                    _pendingRequests.Remove(packet.PacketId);

                    Log($"   Результат отправлен клиенту");
                }
                else
                {
                    Log($"   Клиент для PacketId {packet.PacketId} не найден!", LogLevel.Error);
                }

                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   Прогресс: {_totalTasksCompleted}/{_totalTasksReceived} завершено");

                // Показываем итоговую статистику когда все задачи завершены
                if (_totalTasksCompleted == _totalTasksReceived && _totalTasksReceived > 0)
                {
                    ShowFinalStatistics();
                }

                // Обрабатываем задачи из очереди (если есть)
                if (_taskQueue.Count > 0)
                {
                    Log($"");
                    Log($"   В очереди {_taskQueue.Count} задач, обрабатываю...");
                }
                ProcessTaskQueue();
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки ответа от Slave: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Отправка подтверждения
        /// </summary>
        private void SendAcknowledgment(string ip, int port)
        {
            NetworkMessage ackMessage = new NetworkMessage
            {
                Type = MessageType.Acknowledgment,
                Data = System.Text.Encoding.UTF8.GetBytes("OK")
            };

            _udpService.SendMessageAsync(ackMessage, ip, port);
        }
    }

    /// <summary>
    /// Информация о Slave узле
    /// </summary>
    public class SlaveInfo
    {
        public string SlaveId { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DateTime RegistrationTime { get; set; }
    }

    /// <summary>
    /// Информация о запросе клиента
    /// </summary>
    public class ClientRequestInfo
    {
        public string ClientIp { get; set; }
        public int ClientPort { get; set; }
        public DateTime RequestTime { get; set; }
        public string FileName { get; set; }
    }

    /// <summary>
    /// Данные регистрации Slave
    /// </summary>
    public class SlaveRegistrationData
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
    }

    /// <summary>
    /// Задача в очереди
    /// </summary>
    public class PendingTask
    {
        public NetworkMessage Message { get; set; }
        public string PacketId { get; set; }
        public string FileName { get; set; }
        public ClientRequestInfo ClientInfo { get; set; }
    }
}