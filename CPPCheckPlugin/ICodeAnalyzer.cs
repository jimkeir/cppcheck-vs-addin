﻿using System.Collections.Generic;
using System.Text;
using System;
using EnvDTE;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Management;

namespace VSPackage.CPPCheckPlugin
{
    abstract class ICodeAnalyzer
    {
        protected ICodeAnalyzer()
        {
            _numCores = 0;
            foreach (var item in new System.Management.ManagementObjectSearcher("Select * from Win32_Processor").Get())
            {
                _numCores += int.Parse(item["NumberOfCores"].ToString());
            }
        }

        public abstract void analyze(List<SourceFile> filesToAnalyze, OutputWindowPane outputWindow);

        public void analyze(SourceFile fileToAnalyze, OutputWindowPane outputWindow)
        {
            List<SourceFile> list = new List<SourceFile>();
            list.Add(fileToAnalyze);
            analyze(list, outputWindow);
        }

        protected static HashSet<string> readSuppressions(string projectBasePath)
        {
            string settingsFilePath = projectBasePath + "\\suppressions.cfg";
            HashSet<string> suppressions = new HashSet<string>();
            if (File.Exists(settingsFilePath))
            {
                StreamReader stream = File.OpenText(settingsFilePath);
                string line = null;
                
                string currentGroup = "";
                while ((line = stream.ReadLine()) != null)
                {
                    if (line.Contains("["))
                    {
                        currentGroup = line.Replace("[", "").Replace("]", "");
                        continue; // to the next line
                    }
                    if (currentGroup == "cppcheck")
                    {
                        var components = line.Split(':');
                        if (components.Length >= 2 && components[1] == "*")                          // id and "*" for a file specified
                            components[1] = @"""" + projectBasePath + @"*""";                        // adding path to this specific project
                        else if (components.Length >= 2 && !components[1].StartsWith("*"))           // id and some path without "*"
                            components[1] = @"""" + projectBasePath + @"\\" + components[1] + @""""; // adding path to this specific project

                        string suppression = components[0];
                        if (components.Length > 1)
                            suppression += ":" + components[1];
                        if (components.Length > 2)
                            suppression += ":"+ components[2];

                        if (!string.IsNullOrEmpty(suppression))
                            suppressions.Add(suppression.Replace("\\\\", "\\"));
                    }
                }
            }

            return suppressions;
        }

        protected void run(string analyzerExePath, string arguments, OutputWindowPane outputWindow)
        {
            _outputWindow = outputWindow;
            var t = new System.Threading.Thread(() => analyzerThreadFunc(analyzerExePath, arguments));
            t.Start();
        }

        private void analyzerThreadFunc(string analyzerExePath, string arguments)
        {
            Debug.Assert(!String.IsNullOrEmpty(analyzerExePath) && !String.IsNullOrEmpty(arguments) && _outputWindow != null);
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = analyzerExePath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.CreateNoWindow = true;

            // Set UseShellExecute to false for output redirection.
            process.StartInfo.UseShellExecute = false;

            // Redirect the standard output of the command.   
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            // Set our event handler to asynchronously read the sort output.
            process.OutputDataReceived += new DataReceivedEventHandler(this.analyzerOutputHandler);
            process.ErrorDataReceived += new DataReceivedEventHandler(this.analyzerOutputHandler);

            var timer = Stopwatch.StartNew();
            // Start the process.
            process.Start();

            // Start the asynchronous read of the sort output stream.
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Wait for analysis completion
            process.WaitForExit();
            timer.Stop();
            float timeElapsed = timer.ElapsedMilliseconds / 1000.0f;
            if (process.ExitCode != 0)
                _outputWindow.OutputString("The tool " + analyzerExePath + " has exited with code " + process.ExitCode.ToString() + "\n");
            else
                _outputWindow.OutputString("Analysis completed in " + timeElapsed.ToString() + " seconds\n");
            process.Close();
        }

        private void analyzerOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                String output = outLine.Data;
                _outputWindow.OutputString(output + "\n");
            }
        }

        private OutputWindowPane _outputWindow = null;

        protected int _numCores;
    }
}