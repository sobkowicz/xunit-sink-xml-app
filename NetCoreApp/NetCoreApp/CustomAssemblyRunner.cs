using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runners;

namespace NetCoreApp
{
    public class CustomAssemblyRunner : IDisposable, IMessageSinkWithTypes
    {
        private static readonly Dictionary<Type, string> MessageTypeNames;
        private readonly TestAssemblyConfiguration configuration;
        private readonly IFrontController controller;
        private readonly ManualResetEvent discoveryCompleteEvent = new ManualResetEvent(true);
        private readonly ManualResetEvent executionCompleteEvent = new ManualResetEvent(true);
        private readonly object statusLock = new object();
        private readonly List<ITestCase> testCasesToRun = new List<ITestCase>();
        private bool isDisposed;

        public XElement ExecutionXml { get; private set; } = null;

        static CustomAssemblyRunner()
        {
            MessageTypeNames = new Dictionary<Type, string>();
            AddMessageTypeName<IDiscoveryCompleteMessage>();
            AddMessageTypeName<ITestAssemblyFinished>();
            AddMessageTypeName<ITestCaseDiscoveryMessage>();
        }

        public CustomAssemblyRunner(string assemblyFileName)
        {
            controller = new XunitFrontController(
                AppDomainSupport.Denied, assemblyFileName, diagnosticMessageSink: MessageSinkAdapter.Wrap(this));
            configuration = ConfigReader.Load(assemblyFileName);
        }

        public Action<XElement> OnExecutionComplete { get; set; }

        public AssemblyRunnerStatus Status
        {
            get
            {
                if (!discoveryCompleteEvent.WaitOne(0))
                    return AssemblyRunnerStatus.Discovering;
                if (!executionCompleteEvent.WaitOne(0))
                    return AssemblyRunnerStatus.Executing;

                return AssemblyRunnerStatus.Idle;
            }
        }

        public void Start()
        {
            lock (statusLock)
            {
                if (Status != AssemblyRunnerStatus.Idle)
                    throw new InvalidOperationException("Cannot start the runner");

                testCasesToRun.Clear();
                discoveryCompleteEvent.Reset();
                executionCompleteEvent.Reset();
            }

            ExecutionXml = new XElement("assembly");

            Task.Factory.StartNew(() =>
            {
                var xmlCreationSink = new XmlCreationSinkDecorator(this);

                controller.Find(false, this, GetDiscoveryOptions());
                discoveryCompleteEvent.WaitOne();

                controller.RunTests(testCasesToRun, xmlCreationSink, GetExecutionOptions());
                executionCompleteEvent.WaitOne();

                ExecutionXml = xmlCreationSink.GetBuiltXml();
                OnExecutionComplete?.Invoke(ExecutionXml);
            });
        }

        public void Dispose()
        {
            lock (statusLock)
            {
                if (isDisposed)
                    return;
                if (Status != AssemblyRunnerStatus.Idle)
                    throw new InvalidOperationException("Cannot dispose");
                isDisposed = true;
            }

            controller?.Dispose();
            discoveryCompleteEvent?.Dispose();
            executionCompleteEvent?.Dispose();
        }

        static void AddMessageTypeName<T>() => MessageTypeNames.Add(typeof(T), typeof(T).FullName);

        private void DispatchMessage<TMessage>(
            IMessageSinkMessage message, HashSet<string> messageTypes, Action<TMessage> handler) where TMessage : class
        {
            if (messageTypes == null ||
                !MessageTypeNames.TryGetValue(typeof(TMessage), out var typeName) ||
                !messageTypes.Contains(typeName))
                return;

            handler((TMessage)message);
        }

        bool IMessageSinkWithTypes.OnMessageWithTypes(IMessageSinkMessage message, HashSet<string> messageTypes)
        {
            DispatchMessage<ITestCaseDiscoveryMessage>(message, messageTypes, testDiscovered =>
                testCasesToRun.Add(testDiscovered.TestCase));
            DispatchMessage<IDiscoveryCompleteMessage>(message, messageTypes, discoveryComplete => discoveryCompleteEvent.Set());
            DispatchMessage<ITestAssemblyFinished>(message, messageTypes, assemblyFinished => executionCompleteEvent.Set());

            return true;
        }

        private ITestFrameworkDiscoveryOptions GetDiscoveryOptions()
        {
            var options = TestFrameworkOptions.ForDiscovery(configuration);
            options.SetSynchronousMessageReporting(true);
            return options;
        }

        private ITestFrameworkExecutionOptions GetExecutionOptions()
        {
            var options = TestFrameworkOptions.ForExecution(configuration);
            options.SetSynchronousMessageReporting(true);
            return options;
        }
    }
}
