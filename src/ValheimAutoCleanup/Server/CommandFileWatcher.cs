using System;
using System.IO;
using BepInEx.Logging;

namespace ValheimAutoCleanup.Server
{
    /// <summary>
    /// Runs admin commands written into a plain text file.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// valheim_server.exe never reads standard input - nothing in the game assembly calls
    /// <c>Console.ReadLine</c> - so a headless server has no console to type commands into,
    /// and a <c>Terminal.ConsoleCommand</c> alone is unreachable there. Rented hosts such as
    /// DatHost do give operators a file manager, so a watched command file is the one control
    /// channel that always works, needs no client mod and needs no admin logged into the game.
    ///
    /// Usage: write a subcommand into the file (for example <c>status</c>), save it, and the
    /// plugin runs it within a few seconds, writes the result to the log, and blanks the file
    /// so the command does not repeat.
    ///
    /// The file is polled rather than watched with a FileSystemWatcher: hosted file managers
    /// and FTP uploads frequently replace files in ways that do not raise reliable change
    /// notifications, and a poll every few seconds costs nothing.
    /// </summary>
    internal sealed class CommandFileWatcher
    {
        private const float PollIntervalSeconds = 5f;
        private const long MaxFileBytes = 4096;

        private readonly ManualLogSource _log;
        private readonly PluginConfig _config;
        private readonly Commands _commands;
        private readonly string _path;

        private float _sinceLastPoll;
        private bool _announced;
        private bool _warnedAboutIo;

        internal CommandFileWatcher(ManualLogSource log, PluginConfig config, Commands commands, string configDirectory)
        {
            _log = log;
            _config = config;
            _commands = commands;
            _path = Path.Combine(configDirectory ?? ".", "autocleanup.command.txt");
        }

        internal string Path_ => _path;

        /// <summary>
        /// Creates the command file with a short explanatory header if it does not exist yet,
        /// so an operator browsing the config folder can discover it.
        /// </summary>
        internal void EnsureExists()
        {
            if (!_config.EnableCommandFile)
            {
                return;
            }

            try
            {
                if (File.Exists(_path))
                {
                    return;
                }

                var directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_path, DefaultContents);
                _log.LogInfo("Created the admin command file at " + _path);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not create the admin command file: " + ex.Message);
            }
        }

        /// <summary>Polls the file. Called from the host behaviour's Update.</summary>
        internal void Tick(float deltaTime)
        {
            if (!_config.EnableCommandFile)
            {
                return;
            }

            if (!_announced)
            {
                _announced = true;
                _log.LogInfo(
                    "Watching for admin commands in " + _path +
                    " (write a subcommand such as 'status' into the file and save it).");
            }

            _sinceLastPoll += deltaTime;
            if (_sinceLastPoll < PollIntervalSeconds)
            {
                return;
            }

            _sinceLastPoll = 0f;
            Poll();
        }

        private void Poll()
        {
            string contents;

            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var info = new FileInfo(_path);
                if (info.Length == 0 || info.Length > MaxFileBytes)
                {
                    if (info.Length > MaxFileBytes)
                    {
                        _log.LogWarning("The admin command file is unexpectedly large; ignoring it.");
                    }

                    return;
                }

                contents = File.ReadAllText(_path);
            }
            catch (IOException)
            {
                // The file is probably mid-upload. Try again on the next poll.
                return;
            }
            catch (Exception ex)
            {
                if (!_warnedAboutIo)
                {
                    _warnedAboutIo = true;
                    _log.LogWarning("Could not read the admin command file: " + ex.Message);
                }

                return;
            }

            var command = ExtractCommand(contents);
            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            _log.LogInfo("Running command from file: " + Commands.RootCommand + " " + command);

            foreach (var line in _commands.Execute(command))
            {
                _log.LogInfo("  " + line);
            }

            TryReset();
        }

        /// <summary>
        /// Returns the first non-empty, non-comment line. Everything else in the file - the
        /// explanatory header, blank lines - is ignored.
        /// </summary>
        internal static string ExtractCommand(string contents)
        {
            if (string.IsNullOrEmpty(contents))
            {
                return null;
            }

            var lines = contents.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim().TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                return line;
            }

            return null;
        }

        private void TryReset()
        {
            try
            {
                File.WriteAllText(_path, DefaultContents);
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    "Ran the command but could not clear " + _path + " (" + ex.Message +
                    "). Clear it manually or it will run again.");
            }
        }

        private static string DefaultContents =>
            "# Valheim Auto Cleanup - admin command file" + Environment.NewLine +
            "#" + Environment.NewLine +
            "# Write ONE command on a line of its own, below this header, and save the file." + Environment.NewLine +
            "# The plugin runs it within about five seconds, writes the result to the server" + Environment.NewLine +
            "# log, and then restores this header." + Environment.NewLine +
            "#" + Environment.NewLine +
            "# Available commands:" + Environment.NewLine +
            "#   status   now      dryrun   preview" + Environment.NewLine +
            "#   stats    history  reload   enable    disable   help" + Environment.NewLine +
            "#" + Environment.NewLine +
            "# Lines starting with # are ignored." + Environment.NewLine;
    }
}
