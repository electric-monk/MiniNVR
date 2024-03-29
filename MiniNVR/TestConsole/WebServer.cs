﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.IO;

namespace TestConsole
{
    public class WebServer
    {
        public interface IEndpoint
        {
            void Handle(HttpListenerContext request);
        }

        public static T Load<T>(string name) where T : StaticContent, new()
        {
            T result = new T();
            var fullname = $"{nameof(TestConsole)}.WebContent.{name}";
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using (System.IO.Stream input = assembly.GetManifestResourceStream(fullname))
            {
                using (System.IO.MemoryStream output = new System.IO.MemoryStream())
                {
                    byte[] buffer = new byte[16384];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        output.Write(buffer, 0, count);
                    result.Content = output.ToArray();
                }
            }
            result.MimeType = System.Web.MimeMapping.GetMimeMapping(name);
            if (result.MimeType.StartsWith("text"))
                result.MimeType += "; charset=utf-8";
            return result;
        }

        public class StaticContent : IEndpoint
        {
            public string MimeType { get; set; }
            public byte[] Content { get; set; }

            public StaticContent()
            {
                MimeType = "text/plain";
                Content = new byte[0];
            }

            public static void LoadAll(string path, WebServer server)
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string fullpath = $"{nameof(TestConsole)}.WebContent.{path}";
                foreach (var s in assembly.GetManifestResourceNames().Where(s => s.StartsWith(fullpath))) {
                    string minpath = s.Substring(fullpath.Length);
                    server.AddContent("/" + minpath, Load<StaticContent>(minpath));
                }
            }

            public virtual void Handle(HttpListenerContext request)
            {
                HttpListenerResponse response = request.Response;
                response.ContentLength64 = Content.Length;
                response.ContentType = MimeType;
                response.OutputStream.Write(Content, 0, Content.Length);
                response.Close();
            }
        }

        public static Dictionary<string, string> ParseForm(string s)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (s.Length == 0)
                return result;
            var parts = s.Split('&');
            foreach (var part in parts)
            {
                var kvp = part.Split('=');
                result.Add(Uri.UnescapeDataString(kvp[0]), Uri.UnescapeDataString(kvp[1]));
            }
            return result;
        }

        public static Dictionary<string, string> GetForm(Stream inputStream)
        {
            using (StreamReader reader = new StreamReader(inputStream))
                return ParseForm(reader.ReadToEnd());
        }

        private readonly HttpListener listener;
        private readonly Thread[] workers;
        private readonly Queue<HttpListenerContext> queue;
        private readonly ManualResetEvent stop;
        private readonly Semaphore waiting;
        private readonly Dictionary<string, IEndpoint> content;
        private readonly ReaderWriterLockSlim contentLock;

        private static string MakeError(string title, string subtitle)
        {
            return $"<html><head><title>{title}</title></head><body><h1>{title}</h1>{subtitle}</body></html>";
        }

        public WebServer(int port, int maxThreads, int maxQueue)
        {
            content = new Dictionary<string, IEndpoint>();
            contentLock = new ReaderWriterLockSlim();
            listener = new HttpListener();
            listener.Prefixes.Add(string.Format("http://+:{0}/", port));
            workers = new Thread[maxThreads + 1];
            workers[0] = new Thread(ListenerThread);
            workers[0].Name = $"Server{port}.Listener";
            for (int i = 1; i < workers.Length; i++) {
                workers[i] = new Thread(WorkerThread);
                workers[i].Name = $"Server{port}.Worker{i}";
            }
            Port = port;
            queue = new Queue<HttpListenerContext>(maxQueue);
            stop = new ManualResetEvent(false);
            waiting = new Semaphore(0, maxQueue);
            Error404 = MakeError("404 Not Found", "The requested content was not found.");
            Error500 = MakeError("500 Internal Server Error", "The server cannot complete your request at this time.<br><code>$EXCEPTION$</code>");
        }

        public int Port { get; }

        public string Error404 { get; set; }
        public string Error500 { get; set; }

        public void AddContent(string path, IEndpoint endpoint)
        {
            contentLock.EnterWriteLock();
            try {
                if (content.ContainsKey(path)) {
                    content.Remove(path);
                    if (endpoint == null)
                        Console.WriteLine("Unregistering " + path);
                }
                if (endpoint != null) {
                    content.Add(path, endpoint);
                    Console.WriteLine("Registering " + path);
                }
            }
            finally {
                contentLock.ExitWriteLock();
            }
        }

        private IEndpoint GetContent(string path)
        {
            contentLock.EnterReadLock();
            try {
                IEndpoint result;
                if (!content.TryGetValue(path, out result))
                    result = null;
                return result;
            }
            finally {
                contentLock.ExitReadLock();
            }
        }

        public void Start()
        {
            listener.Start();
            foreach (Thread worker in workers)
                worker.Start();
        }

        public void Stop()
        {
            stop.Set();
            foreach (Thread worker in workers)
                worker.Join();
            listener.Stop();
        }

        private void ListenerThread()
        {
            while (listener.IsListening) {
                var context = listener.BeginGetContext(ListenerAccepted, null);
                if (WaitHandle.WaitAny(new[] { stop, context.AsyncWaitHandle }) == 0)
                    break;
            }
        }

        private void ListenerAccepted(IAsyncResult result)
        {
            HttpListenerContext context = listener.EndGetContext(result);
            try {
                lock (queue) {
                    waiting.Release(1);
                    // Do this second, to avoid gumming up the queue with unexpected contexts that will then prevent expected ones being handled later
                    queue.Enqueue(context);
                }
            }
            catch (Exception e) {
                GenerateError(context, 500, Error500.Replace("$EXCEPTION$", e.ToString()));
            }
        }

        private void WorkerThread()
        {
            WaitHandle[] waits = new WaitHandle[] { stop, waiting };
            while (WaitHandle.WaitAny(waits) != 0) {
                HttpListenerContext context;
                lock (queue) {
                    context = queue.Dequeue();
                }
                // Handle request
                try {
                    IEndpoint location = GetContent(context.Request.Url.LocalPath);
                    if (location == null) {
                        GenerateError(context, 404, Error404);
                    } else {
                        location.Handle(context);
                    }
                }
                catch (Exception e) {
                    Console.WriteLine("Error occurred during web request handling: " + e.ToString());
                }
            }
        }

        private static void GenerateError(HttpListenerContext context, int error, string content)
        {
            HttpListenerResponse response = context.Response;
            response.StatusCode = error;
            response.ContentType = "text/html; charset=utf-8";
            byte[] data = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = data.Length;
            response.OutputStream.Write(data, 0, data.Length);
            response.OutputStream.Close();
        }
    }
}
