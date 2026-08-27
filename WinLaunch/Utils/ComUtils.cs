using System;

namespace WinLaunch
{
    /// <summary>
    /// Late-bound access to the Windows scripting COM objects.
    /// Avoids tlbimp-generated interop assemblies, which the .NET SDK build cannot produce.
    /// </summary>
    public static class ComUtils
    {
        public static dynamic CreateInstance(string progId)
        {
            try
            {
                Type type = Type.GetTypeFromProgID(progId);

                if (type == null)
                    return null;

                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
    }
}
