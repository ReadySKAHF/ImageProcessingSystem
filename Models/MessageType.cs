namespace ImageProcessingSystem.Models
{
    /// <summary>
    /// Типы сообщений в системе
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// Запрос на обработку изображения
        /// </summary>
        ImageRequest,

        /// <summary>
        /// Ответ с обработанным изображением
        /// </summary>
        ImageResponse,

        /// <summary>
        /// Регистрация Slave узла
        /// </summary>
        SlaveRegister,

        /// <summary>
        /// Подтверждение получения
        /// </summary>
        Acknowledgment
    }
}