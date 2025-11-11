using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ImageProcessingSystem.Services
{
    /// <summary>
    /// Сервис применения медианного фильтра к изображениям
    /// </summary>
    public class MedianFilterService
    {
        /// <summary>
        /// Применение медианного фильтра к изображению
        /// </summary>
        /// <param name="imageData">Данные изображения</param>
        /// <param name="filterSize">Размер окна фильтра (по умолчанию 7x7 для сильного эффекта)</param>
        /// <returns>Обработанное изображение</returns>
        public byte[] ApplyMedianFilter(byte[] imageData, int filterSize = 15)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(imageData))
                using (Bitmap originalImage = new Bitmap(ms))
                {
                    Bitmap filteredImage = ApplyMedianFilterToBitmap(originalImage, filterSize);

                    using (MemoryStream outputMs = new MemoryStream())
                    {
                        filteredImage.Save(outputMs, ImageFormat.Png);
                        filteredImage.Dispose();
                        return outputMs.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка применения медианного фильтра: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Применение медианного фильтра к Bitmap
        /// </summary>
        private Bitmap ApplyMedianFilterToBitmap(Bitmap original, int filterSize)
        {
            int width = original.Width;
            int height = original.Height;

            Bitmap result = new Bitmap(width, height);

            // Блокируем биты для быстрого доступа
            BitmapData originalData = original.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            BitmapData resultData = result.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            int bytesPerPixel = 3;
            int stride = originalData.Stride;
            int offset = filterSize / 2;

            unsafe
            {
                byte* originalPtr = (byte*)originalData.Scan0.ToPointer();
                byte* resultPtr = (byte*)resultData.Scan0.ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Массивы для хранения значений окрестности
                        var blueValues = new System.Collections.Generic.List<byte>();
                        var greenValues = new System.Collections.Generic.List<byte>();
                        var redValues = new System.Collections.Generic.List<byte>();

                        // Сбор значений из окна фильтра
                        for (int fy = -offset; fy <= offset; fy++)
                        {
                            for (int fx = -offset; fx <= offset; fx++)
                            {
                                int newX = x + fx;
                                int newY = y + fy;

                                // Проверка границ
                                if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                                {
                                    int pixelOffset = newY * stride + newX * bytesPerPixel;

                                    blueValues.Add(originalPtr[pixelOffset]);
                                    greenValues.Add(originalPtr[pixelOffset + 1]);
                                    redValues.Add(originalPtr[pixelOffset + 2]);
                                }
                            }
                        }

                        // Вычисление медианы для каждого канала
                        byte medianBlue = GetMedian(blueValues);
                        byte medianGreen = GetMedian(greenValues);
                        byte medianRed = GetMedian(redValues);

                        // Установка нового значения пикселя
                        int resultPixelOffset = y * stride + x * bytesPerPixel;
                        resultPtr[resultPixelOffset] = medianBlue;
                        resultPtr[resultPixelOffset + 1] = medianGreen;
                        resultPtr[resultPixelOffset + 2] = medianRed;
                    }
                }
            }

            original.UnlockBits(originalData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Вычисление медианы из списка значений
        /// </summary>
        private byte GetMedian(System.Collections.Generic.List<byte> values)
        {
            if (values.Count == 0)
                return 0;

            values.Sort();
            int middle = values.Count / 2;

            if (values.Count % 2 == 0)
            {
                return (byte)((values[middle - 1] + values[middle]) / 2);
            }
            else
            {
                return values[middle];
            }
        }

        /// <summary>
        /// Получение размеров изображения из байтового массива
        /// </summary>
        public (int width, int height) GetImageDimensions(byte[] imageData)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(imageData))
                using (Bitmap image = new Bitmap(ms))
                {
                    return (image.Width, image.Height);
                }
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// Сжатие изображения с уменьшением качества для передачи по UDP
        /// </summary>
        public byte[] CompressImage(byte[] imageData, long quality = 85L)
        {
            try
            {
                using (MemoryStream inputMs = new MemoryStream(imageData))
                using (Bitmap original = new Bitmap(inputMs))
                using (MemoryStream outputMs = new MemoryStream())
                {
                    // Получаем encoder для JPEG
                    ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);

                    // Настройки качества
                    System.Drawing.Imaging.Encoder encoder = System.Drawing.Imaging.Encoder.Quality;
                    EncoderParameters encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(encoder, quality);

                    // Сохраняем с compression
                    original.Save(outputMs, jpegEncoder, encoderParams);

                    return outputMs.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сжатия изображения: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Получение encoder для формата изображения
        /// </summary>
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}