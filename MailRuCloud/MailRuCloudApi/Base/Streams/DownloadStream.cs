﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace YaR.MailRuCloud.Api.Base.Streams
{
    internal class DownloadStream : Stream
    {
        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(DownloadStream));

        private const int InnerBufferSize = 65536 * 2;

        private readonly Func<long, long, File, HttpWebRequest> _requestGenerator;
        private readonly IList<File> _files;
        private readonly long? _start;
        private readonly long? _end;

        private RingBufferedStream _innerStream;
        private bool _initialized;

        public DownloadStream(Func<long, long, File, HttpWebRequest> requestGenerator, File file, long? start = null, long? end = null)
            : this(requestGenerator, file.Parts, start, end)
        {
        }

        private DownloadStream(Func<long, long, File, HttpWebRequest> requestGenerator, IList<File> files, long? start = null, long? end = null)
        {
            var globalLength = files.Sum(f => f.OriginalSize);

            _requestGenerator = requestGenerator ?? throw new ArgumentNullException(nameof(requestGenerator));
            _files = files;
            _start = start;
            _end = end >= globalLength ? globalLength - 1 : end;

            Length = _start != null && _end != null
                ? _end.Value - _start.Value + 1
                : globalLength;
        }

        public void Open()
        {
            _innerStream = new RingBufferedStream(InnerBufferSize) {ReadTimeout = 15 * 1000, WriteTimeout = 15 * 1000};
            _copyTask = GetFileStream();

            _initialized = true;
        }

        private Task _copyTask;

        private async Task<object> GetFileStream()
        {
            var totalLength = Length;
            long glostart = _start ?? 0;
            long gloend = _end == null || 
                _start == _end && _end == 0 ? totalLength : _end.Value + 1;

            long fileStart = 0;
            long fileEnd = 0;

            foreach (var file in _files)
            {
                var clofile = file;

                fileEnd += clofile.OriginalSize;

                if (glostart >= fileEnd || gloend <= fileStart)
                {
                    fileStart += clofile.OriginalSize;
                    continue;
                }
                
                long clostart = Math.Max(0, glostart - fileStart);
                long cloend = gloend - fileStart - 1;

                await GetWebResponse(clostart, cloend, clofile).ConfigureAwait(false);

                fileStart += file.OriginalSize;
            }

            _innerStream.Flush();

            return _innerStream;
        }

        private async Task<WebResponse> GetWebResponse(long clostart, long cloend, File clofile)
        {
            var request = _requestGenerator(clostart, cloend, clofile);
            Logger.Debug($"HTTP:{request.Method}:{request.RequestUri.AbsoluteUri}");

            try
            {
                var response = await request.GetResponseAsync().ConfigureAwait(false);
                using (var responseStream = response.GetResponseStream())
                {
                    responseStream.ReadTimeout = 15 * 1000;
                    await responseStream.CopyToAsync(_innerStream).ConfigureAwait(false);
                }
                return response;
            }
            catch (Exception e)
            {
                throw;
            }
        }

        private bool _disposed;
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || _disposed) return;

            _disposed = true;

            _innerStream?.Flush();
            _innerStream?.Close();
            Finished?.Invoke();
        }


        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_initialized)
                Open();

            int readed = _innerStream.Read(buffer, offset, count);
            return readed;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead { get; } = true;
        public override bool CanSeek { get; } = true;
        public override bool CanWrite { get; } = false;

        public override long Length { get; }

        public override long Position { get; set; }
        

        public event Action Finished;
    }
}
