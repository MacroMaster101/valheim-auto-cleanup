using System;
using System.Reflection;
using BepInEx.Logging;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// Small, cached reflection helpers.
    ///
    /// Apart from the single documented patch in Patches/ChatMessagePatch.cs, this plugin
    /// does not modify game methods. It does need to *read* two private Valheim fields,
    /// because the game exposes no public accessor for them:
    ///
    ///   ZDOMan.m_objectsByID   - the authoritative table of every ZDO the server holds.
    ///   ItemDrop.s_instances   - the list of ItemDrop components currently instantiated.
    ///
    /// Both lookups happen once and are cached. If either lookup fails (for example after a
    /// Valheim update renames a field), the plugin logs a clear warning and degrades to a
    /// safe fallback rather than throwing; see ItemScanner for the fallback chain.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static FieldInfo InstanceField(Type owner, string name, ManualLogSource log)
        {
            var field = owner?.GetField(name, AnyInstance);
            if (field == null && log != null)
            {
                log.LogWarning(
                    $"Could not find instance field {owner?.Name}.{name}. " +
                    "This usually means Valheim changed internally; the plugin will fall back to a safer, slower path.");
            }

            return field;
        }

        internal static FieldInfo StaticField(Type owner, string name, ManualLogSource log)
        {
            var field = owner?.GetField(name, AnyStatic);
            if (field == null && log != null)
            {
                log.LogWarning(
                    $"Could not find static field {owner?.Name}.{name}. " +
                    "This usually means Valheim changed internally; the plugin will fall back to a safer, slower path.");
            }

            return field;
        }

        internal static T Read<T>(FieldInfo field, object instance) where T : class
        {
            if (field == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(instance) as T;
            }
            catch
            {
                return null;
            }
        }
    }
}
