﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.MSBuild;

public class xunitTests
{
    public class CreateVisitor
    {
        [Fact]
        public void DefaultVisitorIsStandardOutputVisitor()
        {
            var xunit = new Testable_xunit { TeamCity = false };

            var visitor = xunit._CreateVisitor("filename");

            Assert.IsType<StandardOutputVisitor>(visitor);
        }

        [Fact]
        public void VisitorIsTeamCityVisitorWhenTeamCityIsTrue()
        {
            var xunit = new Testable_xunit { TeamCity = true };

            var visitor = xunit._CreateVisitor("filename");

            Assert.IsType<TeamCityVisitor>(visitor);
        }
    }

    public class Execute
    {
        [Fact, PreserveWorkingDirectory]
        public void ChangesCurrentDirectoryWhenWorkingFolderIsNotNull()
        {
            var tempFolder = Environment.GetEnvironmentVariable("TEMP");
            var xunit = new Testable_xunit { WorkingFolder = tempFolder };

            xunit.Execute();

            Assert.Equal(tempFolder, Directory.GetCurrentDirectory());
        }

        [Fact]
        public void DoesNotChangeCurrentDirectoryWhenWorkingFolderIsNull()
        {
            var currentFolder = Directory.GetCurrentDirectory();
            var xunit = new Testable_xunit();

            xunit.Execute();

            Assert.Equal(currentFolder, Directory.GetCurrentDirectory());
        }

        [Fact]
        public void LogsWelcomeBanner()
        {
            var xunit = new Testable_xunit();

            xunit.Execute();

            xunit.BuildEngine.Received().LogMessageEvent(Arg.Is<BuildMessageEventArgs>(bmea => ValidateWelcomeBanner(bmea)));
        }

        private bool ValidateWelcomeBanner(BuildMessageEventArgs eventArgs)
        {
            Assert.Equal(String.Format("xUnit.net MSBuild runner ({0}-bit .NET {1})", IntPtr.Size * 8, Environment.Version), eventArgs.Message);
            Assert.Equal(MessageImportance.High, eventArgs.Importance);
            return true;
        }

        [Fact]
        public void CallsExecuteAssemblyOnceForEachAssembly()
        {
            var visitor = new MSBuildVisitor(null, null, null);
            visitor.Finished.Set();
            var assm1 = new TaskItem(@"C:\Full\Path\1");
            var assm2 = new TaskItem(@"C:\Full\Path\2", new Dictionary<string, string> { { "ConfigFile", @"C:\Config\File" } });
            var xunit = new Testable_xunit { CreateVisitor_Result = visitor };
            xunit.Assemblies = new ITaskItem[] { assm1, assm2 };

            xunit.Execute();

            Assert.Collection(xunit.ExecuteAssembly_Calls,
                call => Assert.Equal(@"C:\Full\Path\1, (null)", call),
                call => Assert.Equal(@"C:\Full\Path\2, C:\Config\File", call)
            );
        }

        [Fact]
        public void ReturnsTrueWhenExitCodeIsZeroAndFailCountIsZero()
        {
            var xunit = new Testable_xunit(exitCode: 0);

            var result = xunit.Execute();

            Assert.True(result);
        }

        [Fact]
        public void ReturnsFalseWhenExitCodeIsNonZero()
        {
            var xunit = new Testable_xunit(exitCode: 1);

            var result = xunit.Execute();

            Assert.False(result);
        }

        [Fact]
        public void ReturnsFalseWhenFailCountIsNonZero()
        {
            var visitor = new MSBuildVisitor(null, null, null) { Failed = 1 };
            visitor.Finished.Set();
            var task = Substitute.For<ITaskItem>();
            task.GetMetadata("FullPath").Returns("C:\\Full\\Path\\Name.dll");
            var xunit = new Testable_xunit { CreateVisitor_Result = visitor, Assemblies = new[] { task } };

            var result = xunit.Execute();

            Assert.False(result);
        }

        [Fact]
        public void WritesXmlToDisk()
        {
            var tempFile = Path.GetTempFileName();

            try
            {
                var visitor = new MSBuildVisitor(null, null, null) { Failed = 1 };
                visitor.Finished.Set();
                var task = Substitute.For<ITaskItem>();
                task.GetMetadata("FullPath").Returns("C:\\Full\\Path\\Name.dll");
                var xmlTaskItem = Substitute.For<ITaskItem>();
                xmlTaskItem.GetMetadata("FullPath").Returns(tempFile);
                var xunit = new Testable_xunit { CreateVisitor_Result = visitor, Assemblies = new[] { task }, Xml = xmlTaskItem };

                xunit.Execute();

                Assert.DoesNotThrow(() => new XmlDocument().Load(tempFile));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WritesXmlV1ToDisk()
        {
            var tempFile = Path.GetTempFileName();

            try
            {
                var visitor = new MSBuildVisitor(null, null, null) { Failed = 1 };
                visitor.Finished.Set();
                var task = Substitute.For<ITaskItem>();
                task.GetMetadata("FullPath").Returns("C:\\Full\\Path\\Name.dll");
                var xmlTaskItem = Substitute.For<ITaskItem>();
                xmlTaskItem.GetMetadata("FullPath").Returns(tempFile);
                var xunit = new Testable_xunit { CreateVisitor_Result = visitor, Assemblies = new[] { task }, XmlV1 = xmlTaskItem };

                xunit.Execute();

                Assert.DoesNotThrow(() => new XmlDocument().Load(tempFile));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void WritesHtmlToDisk()
        {
            var tempFile = Path.GetTempFileName();
            File.Delete(tempFile);

            try
            {
                var visitor = new MSBuildVisitor(null, null, null) { Failed = 1 };
                visitor.Finished.Set();
                var task = Substitute.For<ITaskItem>();
                task.GetMetadata("FullPath").Returns("C:\\Full\\Path\\Name.dll");
                var htmlTaskItem = Substitute.For<ITaskItem>();
                htmlTaskItem.GetMetadata("FullPath").Returns(tempFile);
                var xunit = new Testable_xunit { CreateVisitor_Result = visitor, Assemblies = new[] { task }, Html = htmlTaskItem };

                xunit.Execute();

                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }

    public class ExecuteAssembly
    {
        [Fact]
        public void DisposesOfFrontController()
        {
            var xunit = new Testable_xunit();

            xunit._ExecuteAssembly("assemblyFilename", "configFilename");

            xunit.FrontController.Received().Dispose();
        }

        [Fact]
        public void DiscoversAllTestsInAssembly()
        {
            var xunit = new Testable_xunit();

            xunit._ExecuteAssembly("assemblyFilename", "configFilename");

            xunit.FrontController.Received().Find(false, Arg.Any<IMessageSink>());
        }

        [Fact]
        public void RunsDiscoveredTests()
        {
            var xunit = new Testable_xunit();
            var runTestCases = new List<ITestCase>();
            xunit.FrontController.WhenAny(fc => fc.Run(null, null))
                                 .Do(callInfo =>
                                 {
                                     runTestCases.AddRange((IEnumerable<ITestCase>)callInfo[0]);
                                     ((IMessageSink)callInfo[1]).OnMessage(Substitute.For<ITestAssemblyFinished>());
                                 });

            xunit._ExecuteAssembly("assemblyFilename", "configFilename");

            Assert.Equal(xunit.DiscoveryTestCases, runTestCases);
        }
    }

    public class Testable_xunit : xunit
    {
        public Exception CreateFrontController_Exception;
        public MSBuildVisitor CreateVisitor_Result;
        public readonly List<string> ExecuteAssembly_Calls = new List<string>();
        public readonly IFrontController FrontController;
        public readonly List<ITestCase> DiscoveryTestCases = new List<ITestCase>();

        public Testable_xunit() : this(0) { }

        public Testable_xunit(int exitCode)
        {
            BuildEngine = Substitute.For<IBuildEngine>();

            FrontController = Substitute.For<IFrontController>();
            FrontController.WhenAny(fc => fc.Find(false, null))
                           .Do<bool, IMessageSink>((_, sink) => ReturnDiscoveryMessages(sink));
            FrontController.WhenAny(fc => fc.Find("", false, null))
                           .Do<string, bool, IMessageSink>((_, __, sink) => ReturnDiscoveryMessages(sink));
            FrontController.WhenAny(fc => fc.Run(null, null))
                           .Do<object, IMessageSink>((_, sink) => sink.OnMessage(Substitute.For<ITestAssemblyFinished>()));

            Assemblies = new ITaskItem[0];

            ExitCode = exitCode;
        }

        public IFrontController _CreateFrontController(string assemblyFilename, string configFileName)
        {
            return FrontController;
        }

        protected override IFrontController CreateFrontController(string assemblyFilename, string configFileName)
        {
            if (CreateFrontController_Exception != null)
                throw CreateFrontController_Exception;

            return _CreateFrontController(assemblyFilename, configFileName);
        }

        public MSBuildVisitor _CreateVisitor(string assemblyFileName)
        {
            if (CreateVisitor_Result != null)
                return CreateVisitor_Result;

            return base.CreateVisitor(assemblyFileName, null);
        }

        protected override MSBuildVisitor CreateVisitor(string assemblyFileName, XElement assemblyElement)
        {
            return _CreateVisitor(assemblyFileName);
        }

        public void _ExecuteAssembly(string assemblyFilename, string configFileName)
        {
            base.ExecuteAssembly(assemblyFilename, configFileName);
        }

        protected override XElement ExecuteAssembly(string assemblyFilename, string configFileName)
        {
            ExecuteAssembly_Calls.Add(String.Format("{0}, {1}", assemblyFilename ?? "(null)", configFileName ?? "(null)"));

            return base.ExecuteAssembly(assemblyFilename, configFileName);
        }

        private void ReturnDiscoveryMessages(IMessageSink sink)
        {
            foreach (var testCase in DiscoveryTestCases)
                sink.OnMessage(new TestCaseDiscoveryMessage { TestCase = testCase });

            sink.OnMessage(new DiscoveryCompleteMessage());
        }

        class TestCaseDiscoveryMessage : ITestCaseDiscoveryMessage
        {
            public ITestCase TestCase { get; set; }

            public ITestCollection TestCollection { get; set; }
        }

        class DiscoveryCompleteMessage : IDiscoveryCompleteMessage
        {
            public DiscoveryCompleteMessage()
            {
                Warnings = new string[0];
            }

            public IEnumerable<string> Warnings { get; set; }
        }
    }
}