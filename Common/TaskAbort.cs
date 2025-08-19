using System;

#if YGZIPLIB
namespace YGZipLib.Common
#elif YGMAILLIB
namespace YGMailLib.Zip.Common
#endif
{
    internal class TaskAbort
    {
        internal bool RequestAbort { get; private set; }
        private Exception innerException = null;

        internal static TaskAbort Create()
        {
            return new TaskAbort();
        }

        internal void Abort(Exception exception)
        {
            if (RequestAbort)
                return;

            RequestAbort = true;
            this.innerException = exception; 
        }

        internal void ThrowIfAbortRequested()
        {
            if (RequestAbort)
            {
                throw new TaskAbortException(innerException);
            }
        }
        
        internal class TaskAbortException : Exception
        {

            internal TaskAbortException(Exception ex)
                : base("Other threads have been aborted.", ex) { }

        }


    }

}
