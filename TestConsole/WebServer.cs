using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;

namespace TestConsole
{
    public class WebServer
    {
        public interface Endpoint
        {
            void Handle(HttpListenerContext request);
        }

        public class StaticContent : Endpoint
        {
            public string MimeType { get; set; }
            public byte[] Content { get; set; }
            public Encoding Encoding { get; set; }

            StaticContent()
            {
                MimeType = "text/plain";
                Content = new byte[0];
            }

            public void Handle(HttpListenerContext request)
            {
                HttpListenerResponse response = request.Response;
                response.ContentLength64 = Content.Length;
                response.ContentEncoding = Encoding;
                response.OutputStream.Write(Content, 0, Content.Length);
                response.Close();
            }
        }

        private readonly HttpListener listener;
        private readonly Thread[] workers;
        private readonly Queue<HttpListenerContext> queue;
        private readonly ManualResetEvent stop;
        private readonly Semaphore waiting;
        private readonly Dictionary<string, Endpoint> content;
        private readonly ReaderWriterLockSlim contentLock;

        private static string MakeError(string title, string subtitle)
        {
            return $"<html><head><title>{title}</title></head><body><h1>{title}</h1>{subtitle}</body></html>";
        }

        public WebServer(int port, int maxThreads, int maxQueue)
        {
            content = new Dictionary<string, Endpoint>();
            contentLock = new ReaderWriterLockSlim();
            listener = new HttpListener();
            listener.Prefixes.Add(string.Format("http://+:{0}/", port));
            workers = new Thread[maxThreads + 1];
            workers[0] = new Thread(ListenerThread);
            for (int i = 1; i < workers.Length; i++)
                workers[i] = new Thread(WorkerThread);
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

        public void AddContent(string path, Endpoint endpoint)
        {
            contentLock.EnterWriteLock();
            try {
                if (content.ContainsKey(path))
                    content.Remove(path);
                if (endpoint != null)
                    content.Add(path, endpoint);
            }
            finally {
                contentLock.ExitWriteLock();
            }
        }

        private Endpoint GetContent(string path)
        {
            contentLock.EnterReadLock();
            try {
                Endpoint result;
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
                Endpoint location = GetContent(context.Request.Url.LocalPath);
                if (location == null) {
                    GenerateError(context, 404, Error404);
                } else {
                    location.Handle(context);
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
