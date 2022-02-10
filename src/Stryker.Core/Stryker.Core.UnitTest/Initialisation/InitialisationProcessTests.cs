using Buildalyzer;
using Microsoft.CodeAnalysis;
using Mono.Collections.Generic;
using Moq;
using Stryker.Core.Exceptions;
using Stryker.Core.Initialisation;
using Stryker.Core.Options;
using Stryker.Core.ProjectComponents;
using Stryker.Core.TestRunners;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Stryker.Core.Mutants;
using Xunit;
using Stryker.Core.Initialisation.Buildalyzer;
using Stryker.Core.MutationTest;

namespace Stryker.Core.UnitTest.Initialisation
{
    public class InitialisationProcessTests : TestBase
    {
        private readonly InitialisationProcess _target;
        private readonly ProjectInfo _projectInfo;
        private readonly StrykerOptions _options = new()
        {
            ProjectName = "TheProjectName",
            ProjectVersion = "TheProjectVersion"
        };
        private readonly Mock<ITestRunner> _testRunnerMock = new(MockBehavior.Strict);
        private readonly Mock<IInputFileResolver> _inputFileResolverMock = new(MockBehavior.Strict);
        private readonly Mock<IInitialBuildProcess> _initialBuildProcessMock = new(MockBehavior.Strict);
        private readonly Mock<IInitialTestProcess> _initialTestProcessMock = new(MockBehavior.Strict);
        private readonly Mock<IAssemblyReferenceResolver> _assemblyReferenceResolverMock = new(MockBehavior.Strict);
        private readonly IEnumerable<IAnalyzerResult> _testProjectAnalyzerResultsMock;

        public InitialisationProcessTests()
        {
            _testProjectAnalyzerResultsMock = new List<IAnalyzerResult> {
                TestHelper.SetupProjectAnalyzerResult(projectFilePath: "C://Example/Dir/ProjectFolder").Object
            };
            var folder = new CsharpFolderComposite();
            folder.Add(new CsharpFileLeaf());
            _projectInfo = new ProjectInfo(new MockFileSystem())
            {
                ProjectUnderTestAnalyzerResult = TestHelper
                    .SetupProjectAnalyzerResult(references: System.Array.Empty<string>())
                    .Object,
                TestProjectAnalyzerResults = _testProjectAnalyzerResultsMock,
                ProjectContents = folder
            };

            InitSetupMocks();

            _target = new InitialisationProcess(
                _inputFileResolverMock.Object,
                _initialBuildProcessMock.Object,
                _initialTestProcessMock.Object,
                _testRunnerMock.Object,
                _assemblyReferenceResolverMock.Object);
        }

        private void InitSetupMocks()
        {
            _testRunnerMock.Setup(x => x.RunAll(It.IsAny<ITimeoutValueCalculator>(), null, null))
                            .Returns(new TestRunResult(true)); // testrun is successful
            _testRunnerMock.Setup(x => x.DiscoverTests()).Returns(new TestSet());
            _testRunnerMock.Setup(x => x.Dispose());
            _initialTestProcessMock.Setup(x => x.InitialTest(It.IsAny<StrykerOptions>(), It.IsAny<ITestRunner>())).Returns(new InitialTestRun(new TestRunResult(true), new TimeoutValueCalculator(1)));
            _initialBuildProcessMock.Setup(x => x.InitialBuild(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), null));
            _assemblyReferenceResolverMock.Setup(x => x.LoadProjectReferences(It.IsAny<string[]>()))
                .Returns(Enumerable.Empty<PortableExecutableReference>())
                .Verifiable();

            _inputFileResolverMock.Setup(x => x.ResolveInput(It.IsAny<StrykerOptions>()))
                .Returns(_projectInfo);
        }

        [Fact]
        public void InitialisationProcess_ShouldCallNeededResolvers()
        {
            var initializeResult = _target.Initialize(_options);

            VerifyCorrectInitialization(initializeResult, _options);
        }

        [Fact]
        public void InitialisationProcess_WithNoBuildOption_ShouldNotBuldTestProjects()
        {
            var options = new StrykerOptions
            {
                ProjectName = "TheProjectName",
                ProjectVersion = "TheProjectVersion",
                NoBuild = true
            };

            var initializeResult = _target.Initialize(options);

            VerifyCorrectInitialization(initializeResult, options);
        }

        [Fact]
        public void InitialisationProcess_ShouldThrowOnFailedInitialTestRun()
        {
            _initialTestProcessMock.Setup(x => x.InitialTest(It.IsAny<StrykerOptions>(), It.IsAny<ITestRunner>())).Throws(new InputException("")); // failing test

            var initializeResult = _target.Initialize(_options);
            VerifyCorrectInitialization(initializeResult, _options);

            _ = Assert.Throws<InputException>(() => _target.InitialTest(_options));

            _initialTestProcessMock.Verify(x => x.InitialTest(It.IsAny<StrykerOptions>(), _testRunnerMock.Object), Times.Once);
        }

        private void VerifyCorrectInitialization(MutationTestInput initializeResult, StrykerOptions options)
        {
            Assert.NotNull(initializeResult);
            Assert.StrictEqual(initializeResult.ProjectInfo, _projectInfo);
            Assert.StrictEqual(initializeResult.TestRunner, _testRunnerMock.Object);
            Assert.Equal(initializeResult.AssemblyReferences, Enumerable.Empty<PortableExecutableReference>());

            _inputFileResolverMock.Verify(x => x.ResolveInput(It.IsAny<StrykerOptions>()), Times.Once);
            _assemblyReferenceResolverMock.Verify();
            if (options.NoBuild)
            {
                _initialBuildProcessMock.Verify(x => x.InitialBuild(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            }
            else
            {
                foreach (var testProject in _testProjectAnalyzerResultsMock)
                {
                    var isFullFramework = testProject.GetTargetFramework() == Framework.DotNetClassic;

                    _initialBuildProcessMock.Verify(x =>
                        x.InitialBuild(isFullFramework, testProject.ProjectFilePath, options.SolutionPath, options.MsBuildPath), Times.Exactly(_testProjectAnalyzerResultsMock.Count()));
                }
            }
        }
    }
}
