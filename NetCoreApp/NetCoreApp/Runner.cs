using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;

namespace NetCoreApp
{
    public class Runner
    {
        private AutoResetEvent runTestsFinished = new AutoResetEvent(false);

        public void RunTests()
        {
            // number of loops to increase possibility of get empty XML
            var loopsCount = 1000;

            // save to memory stream first - if save directly to file then
            // this operation is slow and in this time XML will be filled
            using (var ms = new MemoryStream())
            {
                for (int i = 0; i < loopsCount; i++)
                    RunAssemblyTests(ms);

                using (var fs = File.Create(GetOutputFileName()))
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    ms.CopyTo(fs);
                }
            }
        }

        private XElement RunAssemblyTests(MemoryStream ms)
        {
            runTestsFinished = new AutoResetEvent(false);
            var runner = new CustomAssemblyRunner(GetTestsAssemblyLocation())
            {
                OnExecutionComplete = OnExecutionComplete,
            };
            runner.Start();
            runTestsFinished.WaitOne();

            var resultXml = runner.ExecutionXml;
            runTestsFinished.Dispose();

            if (resultXml.Descendants("test").Count() == 0)
            {
                // sometimes save to stream is not enough fast and we save already generated XML
                WriteToStream(ms, resultXml.ToString(SaveOptions.DisableFormatting));
                Console.WriteLine("Found empty results XML");
            }

            return resultXml;
        }

        private void WriteToStream(MemoryStream ms, string content)
        {
            var formattedContent = string.Format("{0}{1}{1}", content, Environment.NewLine);
            var data = System.Text.Encoding.UTF8.GetBytes(formattedContent);
            ms.Write(data, 0, data.Length);
        }

        private void OnExecutionComplete(XElement xml)
        {
            runTestsFinished.Set();
        }

        private string GetTestsAssemblyLocation()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var file = Directory.GetFiles(Path.GetDirectoryName(basePath), "*ExternalTests.dll").Single(); ;
            var loadedAssembly = Assembly.Load(Path.GetFileNameWithoutExtension(file));
            return loadedAssembly.Location;
        }

        private string GetOutputFileName()
        {
            var path = "results";
            var fileName = $"{DateTime.Now:yyyy-MM-dd--HH-mm-ss}.txt";
            Directory.CreateDirectory(path);
            return Path.Combine(path, fileName);
        }
    }
}
