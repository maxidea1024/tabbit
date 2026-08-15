// Just enough of UnityEngine to compile the generated code's Unity branches.
//
// Not a fake and not something to run: nothing here has a body worth calling. It exists so
// the compile check can select the branches Unity would take and have the compiler resolve
// the members they name - which is the whole question. Before this the Unity branches were
// never compiled by anything, and the one that reads a URL through UnityWebRequest could not
// be compiled at all without an engine, so it was the least checked code in the repository
// and the one that runs on Android and WebGL.
//
// Compiled only when the check is given Unity symbols, so a plain build never sees it.
//
// Only one shape of the result check is here now. Unity 6.5 is the floor, so
// UnityWebRequest.Result is simply there and the isNetworkError pair it replaced went with
// the versions that had it.

#if UNITY_5_3_OR_NEWER

using System;

namespace UnityEngine
{
    /// <summary>Stands in for the coroutine handle a web request hands back.</summary>
    public sealed class AsyncOperation
    {
        public bool isDone => throw new NotImplementedException("A stub, for compiling only.");
    }

    /// <summary>The updater writes its cache under persistentDataPath.</summary>
    public static class Application
    {
        public static string persistentDataPath
            => throw new NotImplementedException("A stub, for compiling only.");
    }

    /// <summary>When the engine calls a method it found by this attribute.</summary>
    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad,
        BeforeSceneLoad,
        BeforeSplashScreen,
        SubsystemRegistration,
    }

    /// <summary>
    /// How the adapter installs itself: the engine calls the method before the first scene,
    /// so a consuming project has nothing to wire up.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }

        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}

namespace UnityEngine.Networking
{
    public sealed class DownloadHandler
    {
        public byte[] data => throw new NotImplementedException("A stub, for compiling only.");
    }

    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result
        {
            InProgress,
            Success,
            ConnectionError,
            ProtocolError,
            DataProcessingError,
        }

        public static UnityWebRequest Get(string uri)
            => throw new NotImplementedException("A stub, for compiling only.");

        public UnityEngine.AsyncOperation SendWebRequest()
            => throw new NotImplementedException("A stub, for compiling only.");

        public Result result => throw new NotImplementedException("A stub, for compiling only.");

        public string error => throw new NotImplementedException("A stub, for compiling only.");

        // What the updater needs beyond a plain read: a bound on how long a request may
        // take, the status code behind a failure, and a way to stop one that was cancelled.
        public int timeout { get; set; }

        public long responseCode => throw new NotImplementedException("A stub, for compiling only.");

        public void Abort() { }

        public DownloadHandler downloadHandler
            => throw new NotImplementedException("A stub, for compiling only.");

        public void Dispose() { }
    }
}

#endif
