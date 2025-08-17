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

        internal static TaskAbort Create
        {
            get
            {
                return new TaskAbort();
            }
        }

        internal void Abort()
        {
            if (RequestAbort)
                return;

            RequestAbort = true;
        }

        internal void ThrowIfAbortRequested()
        {
            if (RequestAbort)
            {
                throw new TaskAbortException();
            }
        }

        internal class TaskAbortException : Exception
        {

            internal TaskAbortException()
                : base("Other threads have been aborted.") { }

        }


    }

}
