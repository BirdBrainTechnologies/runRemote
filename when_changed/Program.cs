using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Diagnostics;

/*
 * Author: Tom Lauwers
 * 
 * Description: This program checks if a python or java file has been added or changed in a given folder,
 * then compiles & runs that file. It also logs the error and standard output to a file. 
 * 
 * This program was created to allow students to program BirdBrain Technologies robots remotely by 
 * adding java or python files in a shared network folder.
 * 
 * The program is based on the when_changed program, by Ben Blamey:
 * https://github.com/benblamey/when_changed
 * He based his implementation on: http://msdn.microsoft.com/en-GB/library/system.io.filesystemwatcher.changed.aspx
 */ 

namespace when_changed
{
    class Program
    {

        private static State m_state;
        private static Object m_state_lock = new Object();
        // Flag to decide if we are compiling java or python code
        private static bool runPython = false;

        public static void Main()
        {
            Run();
        }

        public static void Run()
        {
            // Watch the current directory
            String thingToWatch = Directory.GetCurrentDirectory(); 
            FileSystemWatcher watcher = createWatcher(thingToWatch);
            
            // Add event handlers.
            watcher.Changed += new FileSystemEventHandler(OnChanged);
            watcher.Created += new FileSystemEventHandler(OnChanged);
            //watcher.Deleted += new FileSystemEventHandler(OnChanged); we don't do anything with deletions
            watcher.Renamed += new RenamedEventHandler(OnRenamed);

            // Begin watching.
            watcher.EnableRaisingEvents = true;

            // Wait for the user to quit the program.
            Console.WriteLine("when_changed now watching: " + watcher.Path +"\\"+ watcher.Filter);

            Console.WriteLine("Ctrl-C to quit.");
            // Original code to force running a file - commented out
            while (true)
            {
                var key = Console.ReadKey(true);
                /*if (key.Key == ConsoleKey.F)
                {
                    Console.WriteLine("Forcing run...");
                    runCmd("");
                }*/
            }
        }

        public static FileSystemWatcher createWatcher(String thingToWatch)
        {
            String dirToWatch; // The directory to watch.
            String fileFilter; // The filter for which files in that directory to watch.

            // Set the directory to where we are running, and watch for python or java files
            dirToWatch = Directory.GetCurrentDirectory();
            if (runPython)
            {
                fileFilter = "*.py";
            }
            else
            {
                fileFilter = "*.java";
            }

            // Create a new FileSystemWatcher and set its properties.
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = dirToWatch;
            /* Watch for changes in LastAccess and LastWrite times, and
               the renaming of files or directories. */
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            watcher.Filter = fileFilter;
            // Allow subdirectories for Python only
            watcher.IncludeSubdirectories = runPython; // fileFilter.Contains("**");
            
            return watcher;
        }


        // Define the event handlers. 
        private static void OnChanged(object source, FileSystemEventArgs e)
        {
            // Specify what is done when a file is changed, created, or deleted.
            Console.WriteLine(DateTime.Now.ToShortTimeString() + " File: " + e.FullPath + " " + e.ChangeType);
            runCmd(e.FullPath);
        }

        private static void OnRenamed(object source, RenamedEventArgs e)
        {
            // Specify what is done when a file is renamed.
            Console.WriteLine(DateTime.Now.ToShortTimeString() + "File: {0} renamed to {1}.", e.OldFullPath, e.FullPath);
            runCmd(e.FullPath);
        }

        private static void runCmd(string changed_file)
        {
            // When a file is updated, we often get a flurry of updates in a single second.
            lock (m_state_lock) {
                switch (m_state)
                {
                    case State.Executing:
                        // Oh noeeees - it changed while we were executing. do it again straight after.
                        Console.WriteLine(" -- output will be dirty - will run again soon...");
                        m_state = State.ExecutingDirty;
                        break;
                    case State.ExecutingDirty:
                        // Leave the flag dirty.
                        break;
                    case State.WaitingToExecute:
                        break;
                    case State.Watching:
                        // Start a new thread to delay and run the command, meanwhile subsequent nots. ignored.
                        m_state = State.WaitingToExecute;
                        Thread t = new Thread(new ParameterizedThreadStart(threadRun));
                        t.Start(changed_file);
                        break;
                    default:
                        throw new InvalidProgramException("argh! enum values?!");
                }
            }

        }

        private static void threadRun(object changed_file)
        {
            string changedfile = (string)changed_file;
            Boolean again = true;
            while (again)
            {
                waitThenRun(changedfile);

                // When a file is updated, we often get a flurry of updates in a single second.
                lock (m_state_lock)
                {
                    switch (m_state)
                    {
                        case State.Executing:
                            // no subsequent changes - output ok (ish)
                            m_state = State.Watching;
                            again = false;
                            break;
                        case State.ExecutingDirty:
                            // Clean the dirty flag, and repeat.
                            m_state = State.WaitingToExecute;
                            again = true;
                            break;
                            // This used to throw an exception, it no longer does because we are writing to log files
                            // however, this does mean some changes are not executed on
                        case State.WaitingToExecute:
                            //throw new InvalidProgramException("shouldn't happen - waiting to execute");
                            m_state = State.Watching;
                            again = false;
                            break;
                        case State.Watching:
                            throw new InvalidProgramException("shouldn't happen - watching");
                        default:
                            throw new InvalidProgramException("argh! enum values?!");
                    }
                }
            }
        }

        private static void waitThenRun(string filechanged)
        {
            // Get rid of the file path, needed for Java and for the log.txt file
            string justFileName = filechanged.Substring(filechanged.LastIndexOf("\\")+1);

            Console.WriteLine("Running this file: " + justFileName);

            // Wait for things to calm down.
            Thread.Sleep(1500);

            if (runPython)
            {
                var p = new Process();
                
                p.StartInfo.FileName = "CMD.EXE"; // Run the program from the command prompt
                p.StartInfo.Arguments = "/c python.exe " + "\"" + filechanged + "\"";

                // Don't open a new shell, and redirect the output streams
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                string errorOut = null;
                // Asynchronously start thread to read standard error - we can't synchronously read both standard output and error
                p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
                { errorOut += e.Data; });

                p.Start();
                // Start reading the error log
                p.BeginErrorReadLine();

                Console.WriteLine("Error/Console Output: ");
                // Synchronously read the standard output of the spawned process.
                StreamReader reader = p.StandardOutput;
                string standardOut = reader.ReadToEnd();

                // Write the redirected output to this application's window.
                Console.WriteLine(standardOut);
                p.WaitForExit();
                // Logging output to a text file
                String path = justFileName+"_Log.txt";
                using (StreamWriter sr = File.AppendText(path))
                {
                    sr.WriteLine(DateTime.Now.ToShortTimeString());
                    sr.WriteLine("Running file: " + filechanged);
                    sr.WriteLine(standardOut);
                    sr.WriteLine(errorOut);
                    sr.WriteLine("");
                    sr.Close();
                }
            }
            else
            {
                // New code - simplified since we are just running Java
                // Step 1 - compile the file
                var p = new Process();
                p.StartInfo.FileName = "CMD.EXE";
                p.StartInfo.Arguments = "/c javac.exe " + "\"" + filechanged + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                string errorOut = null;
                // Asynchronously start thread to read standard error
                p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
                { errorOut += e.Data; });

                p.Start();
                // Start reading the error log
                p.BeginErrorReadLine();

                Console.WriteLine("Error/Console Output: ");
                // Synchronously read the standard output of the spawned process.
                StreamReader reader = p.StandardOutput;
                string standardOut = reader.ReadToEnd();

                // Write the redirected output to this application's window.
                Console.WriteLine(standardOut);
                p.WaitForExit();
                // Logging output to a text file
                String path = justFileName + "_Log.txt";
                using (StreamWriter sr = File.AppendText(path))
                {
                    sr.WriteLine(DateTime.Now.ToShortTimeString());
                    sr.WriteLine("Compiling file: " + filechanged);
                    sr.WriteLine(standardOut);
                    sr.WriteLine(errorOut);
                    sr.Close();
                }

                // Checking to see if the file compiled, and only running it if it did compile
                string pathToClassFile = justFileName.Substring(0, justFileName.Length - 5) + ".class";

                if (File.Exists(pathToClassFile))
                {
                    // Step 2 - run the file
                    var pRun = new Process();
                    pRun.StartInfo.FileName = "CMD.EXE";
                    // Removed the .java extension and used a relative path to run the file
                    pRun.StartInfo.Arguments = "/c java.exe " + justFileName.Substring(0, justFileName.Length - 5);
                    pRun.StartInfo.UseShellExecute = false;
                    pRun.StartInfo.RedirectStandardOutput = true;
                    pRun.StartInfo.RedirectStandardError = true;
                    // Reset error
                    errorOut = null;
                    // Asynchronously start thread to read standard error
                    pRun.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
                    { errorOut += e.Data; });

                    pRun.Start();
                    // Start reading the error log
                    pRun.BeginErrorReadLine();

                    Console.WriteLine("Error/Console Output: ");
                    // Synchronously read the standard output of the spawned process.
                    reader = pRun.StandardOutput;
                    standardOut = reader.ReadToEnd();

                    // Write the redirected output to this application's window.
                    Console.WriteLine(standardOut);
                    pRun.WaitForExit();
                    // Logging output to a text file
                    path = justFileName + "_Log.txt";
                    using (StreamWriter sr = File.AppendText(path))
                    {
                        sr.WriteLine("Running file: " + filechanged);
                        sr.WriteLine(standardOut);
                        sr.WriteLine(errorOut);
                        sr.WriteLine("");
                        sr.Close();
                    }
                    // Now delete the class file
                    File.Delete(pathToClassFile);
                }
            }


        }



    }
}
