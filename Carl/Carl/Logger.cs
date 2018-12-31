using System;
using System.Collections.Generic;
using System.Text;

namespace Carl
{
    class Logger
    {
        public static void Info(string message)
        {
            Log(message);
        }

        public static void Error(string message)
        {
            Log($"Error!: {message}");
        }

        public static void Error(string message, Exception e)
        {
            Log($"Error!: {message}, exception:{(e == null ? "no message" : e.Message)} \n\rStack:{(e == null ? "no stack" : e.StackTrace)}");
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
