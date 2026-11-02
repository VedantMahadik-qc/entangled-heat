using System;
using System.IO;

namespace EntangledHeat.Core
{
    /// <summary>Thread-safe file log. Never pass the API key to it.</summary>
    public static class Log
    {
        static readonly object Gate = new object();
        static string _path;

        public static void Init(string path)
        {
            lock (Gate)
            {
                _path = path;
                File.WriteAllText(path, $"Entangled Heat log started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            }
        }

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message) => Write("ERROR", message);

        static void Write(string level, string message)
        {
            lock (Gate)
            {
                if (_path == null)
                    return;
                try
                {
                    File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
                }
                catch (IOException)
                {
                    // Logging must never take the game down.
                }
            }
        }
    }
}
