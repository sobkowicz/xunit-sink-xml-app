using System.Collections.Generic;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace NetCoreApp
{
    public class XmlCreationSinkDecorator : IMessageSinkWithTypes
    {
        private readonly DelegatingExecutionSummarySink executionSummarySink;
        private readonly DelegatingXmlCreationSink xmlCreationSink;
        private readonly XElement assemblyElement;

        public XmlCreationSinkDecorator(IMessageSinkWithTypes innerSink)
        {
            assemblyElement = new XElement("assembly");
            executionSummarySink = new DelegatingExecutionSummarySink(innerSink);
            xmlCreationSink = new DelegatingXmlCreationSink(executionSummarySink, assemblyElement);
        }

        public bool OnMessageWithTypes(IMessageSinkMessage message, HashSet<string> messageTypes)
        {
            return xmlCreationSink.OnMessageWithTypes(message, messageTypes)
                && message.Dispatch<ITestCaseDiscoveryMessage>(messageTypes, HandleTestCaseDiscoveryMessage);
        }

        public void Dispose()
        {
            xmlCreationSink?.Dispose();
            executionSummarySink?.Dispose();
        }

        public XElement GetBuiltXml()
        {
            // here is the problem - sometimes XElement with results is empty
            return assemblyElement;
        }

        private void HandleTestCaseDiscoveryMessage(MessageHandlerArgs<ITestCaseDiscoveryMessage> args)
        {
            // some my stuff
        }
    }
}
