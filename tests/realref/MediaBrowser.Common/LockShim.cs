// Stand-in for .NET 9's System.Threading.Lock, which Jellyfin's source locks on. It lives in the
// plugin namespace, which wins over `using System.Threading;`: a type named System.Threading.Lock
// makes recent compilers (SDK 8.0.4xx and later) require the C# 13 "lock object" feature.
namespace MediaBrowser.Common.Plugins { internal sealed class Lock { } }
