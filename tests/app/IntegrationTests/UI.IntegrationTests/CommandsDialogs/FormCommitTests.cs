﻿using System.Collections;
using System.ComponentModel.Design;
using CommonTestUtils;
using FluentAssertions;
using GitCommands;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitExtUtils.GitUI.Theming;
using GitUI;
using GitUI.CommandsDialogs;
using GitUI.Editor;
using GitUI.ScriptsEngine;
using GitUI.UserControls;
using ICSharpCode.TextEditor;
using NSubstitute;

namespace GitExtensions.UITests.CommandsDialogs
{
    [Apartment(ApartmentState.STA)]
    public class FormCommitTests
    {
        // Created once for the fixture
        private ReferenceRepository _referenceRepository;

        // Track the original setting value
        private bool _provideAutocompletion;
        private bool _showAvailableDiffTools;

        // Created once for each test
        private GitUICommands _commands;

        [SetUp]
        public void SetUp()
        {
            ReferenceRepository.ResetRepo(ref _referenceRepository);

            ServiceContainer serviceContainer = GlobalServiceContainer.CreateDefaultMockServiceContainer();
            serviceContainer.RemoveService<IScriptsRunner>();

            IScriptsRunner scriptsRunner = Substitute.For<IScriptsRunner>();
            scriptsRunner.RunEventScripts(Arg.Any<ScriptEvent>(), Arg.Any<FormCommit>()).Returns(true);
            serviceContainer.AddService(scriptsRunner);

            _commands = new GitUICommands(serviceContainer, _referenceRepository.Module);
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Remember the current setting...
            _provideAutocompletion = AppSettings.ProvideAutocompletion;
            _showAvailableDiffTools = AppSettings.ShowAvailableDiffTools;

            // ...and stop loading auto completion and custom diff tools
            AppSettings.ProvideAutocompletion = false;
            AppSettings.ShowAvailableDiffTools = false;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AppSettings.ProvideAutocompletion = _provideAutocompletion;
            AppSettings.ShowAvailableDiffTools = _showAvailableDiffTools;
            _referenceRepository.Dispose();
        }

        [Test]
        public void Should_show_committer_info_on_open()
        {
            RunFormTest(async form =>
            {
                ToolStripStatusLabel commitAuthorStatus = form.GetTestAccessor().CommitAuthorStatusToolStripStatusLabel;

                await Task.Delay(1000);
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ClassicAssert.AreEqual("Committer author <author@mail.com>", commitAuthorStatus.Text);
            });
        }

        [Test]
        public void Should_not_update_committer_info_on_form_activated()
        {
            RunFormTest(async form =>
            {
                ToolStripStatusLabel commitAuthorStatus = form.GetTestAccessor().CommitAuthorStatusToolStripStatusLabel;

                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ClassicAssert.AreEqual("Committer author <author@mail.com>", commitAuthorStatus.Text);

                using (Form tempForm = new())
                {
                    tempForm.Owner = form;
                    tempForm.Show();
                    tempForm.Focus();

                    _referenceRepository.Module.GitExecutable.GetOutput(@"config user.name ""new author""");
                    _referenceRepository.Module.GitExecutable.GetOutput(@"config user.email ""new_author@mail.com""");
                }

                form.Focus();
                Application.DoEvents();

                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ClassicAssert.AreEqual("Committer author <author@mail.com>", commitAuthorStatus.Text);
            });
        }

        [Test]
        public void Should_display_branch_and_no_remote_info_in_statusbar()
        {
            _referenceRepository.CheckoutBranch("master");
            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ToolStripStatusLabel currentBranchNameLabelStatus = form.GetTestAccessor().CurrentBranchNameLabelStatus;
                ToolStripStatusLabel remoteNameLabelStatus = form.GetTestAccessor().RemoteNameLabelStatus;

                ClassicAssert.AreEqual("master →", currentBranchNameLabelStatus.Text);
                ClassicAssert.AreEqual("(remote not configured)", remoteNameLabelStatus.Text);
            });
        }

        [Test]
        public void Should_display_detached_head_info_in_statusbar()
        {
            _referenceRepository.CheckoutRevision();
            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ToolStripStatusLabel currentBranchNameLabelStatus = form.GetTestAccessor().CurrentBranchNameLabelStatus;
                ToolStripStatusLabel remoteNameLabelStatus = form.GetTestAccessor().RemoteNameLabelStatus;

                // For a yet unknown cause randomly, the wait in UITest.RunForm does not suffice.
                if (!string.IsNullOrEmpty(remoteNameLabelStatus.Text))
                {
                    Console.WriteLine($"{nameof(Should_display_detached_head_info_in_statusbar)} waits again");
                    await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);
                }

                ClassicAssert.AreEqual("(no branch)", currentBranchNameLabelStatus.Text);
                ClassicAssert.AreEqual(string.Empty, remoteNameLabelStatus.Text);
            });
        }

        [Test]
        public void Should_display_branch_and_remote_info_in_statusbar()
        {
            _referenceRepository.CreateRemoteForMasterBranch();
            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ToolStripStatusLabel currentBranchNameLabelStatus = form.GetTestAccessor().CurrentBranchNameLabelStatus;
                ToolStripStatusLabel remoteNameLabelStatus = form.GetTestAccessor().RemoteNameLabelStatus;

                ClassicAssert.AreEqual("master →", currentBranchNameLabelStatus.Text);
                ClassicAssert.AreEqual("origin/master", remoteNameLabelStatus.Text);
            });
        }

        [Test]
        public void PreserveCommitMessageOnReopen()
        {
            string generatedCommitMessage = Guid.NewGuid().ToString();

            RunFormTest(form =>
            {
                ClassicAssert.IsEmpty(form.GetTestAccessor().Message.Text);
                form.GetTestAccessor().Message.Text = generatedCommitMessage;
            });

            RunFormTest(form =>
            {
                ClassicAssert.AreEqual(generatedCommitMessage, form.GetTestAccessor().Message.Text);
            });
        }

        [TestCase(CommitKind.Fixup)]
        [TestCase(CommitKind.Squash)]
        public void DoNotPreserveCommitMessageOnReopenFromSpecialCommit(CommitKind commitKind)
        {
            string generatedCommitMessage = Guid.NewGuid().ToString();

            RunFormTest(
                form =>
                {
                    string prefix = commitKind.ToString().ToLowerInvariant();
                    ClassicAssert.AreEqual($"{prefix}! A commit message", form.GetTestAccessor().Message.Text);
                    form.GetTestAccessor().Message.Text = generatedCommitMessage;
                },
                commitKind);

            RunFormTest(form =>
            {
                ClassicAssert.IsEmpty(form.GetTestAccessor().Message.Text);
            });
        }

        [Test]
        public void PreserveCommitMessageOnReopenFromAmendCommit()
        {
            string oldCommitMessage = _referenceRepository.Module.GetRevision().Body;
            string newCommitMessageWithAmend = $"amend! {oldCommitMessage}\n\nNew commit message";

            RunFormTest(
                form =>
                {
                    ClassicAssert.AreEqual($"amend! {oldCommitMessage}\n\n{oldCommitMessage}", form.GetTestAccessor().Message.Text);
                    form.GetTestAccessor().Message.Text = newCommitMessageWithAmend;
                },
                CommitKind.Amend);

            RunFormTest(form =>
            {
                ClassicAssert.AreEqual(newCommitMessageWithAmend, form.GetTestAccessor().Message.Text);
            });
        }

        [Test]
        public void SelectMessageFromHistory()
        {
            const string lastCommitMessage = "last commit message";
            AppSettings.LastCommitMessage = lastCommitMessage;

            RunFormTest(form =>
            {
                ToolStripDropDownButton commitMessageToolStripMenuItem = form.GetTestAccessor().CommitMessageToolStripMenuItem;

                // Verify the message appears correctly
                commitMessageToolStripMenuItem.ShowDropDown();
                commitMessageToolStripMenuItem.DropDownItems[0].Text.Should().Be(lastCommitMessage);

                // Verify the message is selected correctly
                commitMessageToolStripMenuItem.DropDownItems[0].PerformClick();
                form.GetTestAccessor().Message.Text.Should().Be(lastCommitMessage);
            });
        }

        [Test]
        public void Should_handle_well_commit_message_in_commit_message_menu()
        {
            const string lastCommitMessage = "last commit message";
            AppSettings.LastCommitMessage = lastCommitMessage;

            _referenceRepository.CreateCommit("Only first line\n\nof a multi-line commit message\nmust be displayed in the menu");
            _referenceRepository.CreateCommit("Too long commit message that should be shorten because first line of a commit message is only 50 chars long");
            RunFormTest(form =>
            {
                ToolStripDropDownButton commitMessageToolStripMenuItem = form.GetTestAccessor().CommitMessageToolStripMenuItem;

                // Verify the message appears correctly
                commitMessageToolStripMenuItem.ShowDropDown();
                commitMessageToolStripMenuItem.DropDownItems[0].Text.Should().Be(lastCommitMessage);
                commitMessageToolStripMenuItem.DropDownItems[1].Text.Should().Be("Too long commit message that should be shorten because first line of ...");
                commitMessageToolStripMenuItem.DropDownItems[2].Text.Should().Be("Only first line");
            });
        }

        [SetCulture("en-US")]
        [SetUICulture("en-US")]
        [Test]
        public void Should_stage_only_filtered_on_StageAll()
        {
            _referenceRepository.CreateRepoFile("file1A.txt", "Test");
            _referenceRepository.CreateRepoFile("file1B.txt", "Test");
            _referenceRepository.CreateRepoFile("file2.txt", "Test");

            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ClassicAssert.AreEqual("Stage all", form.GetTestAccessor().StageAllToolItem.ToolTipText);
            });

            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                FormCommit.TestAccessor testform = form.GetTestAccessor();

                testform.UnstagedList.ClearSelected();
                testform.UnstagedList.SetFilter("file1");

                ClassicAssert.AreEqual("Stage filtered", testform.StageAllToolItem.ToolTipText);

                testform.StageAllToolItem.PerformClick();

                bool fileNotMatchedByFilterIsStillUnstaged = testform.UnstagedList.AllItems.Any(i => i.Item.Name == "file2.txt");

                ClassicAssert.AreEqual(2, testform.StagedList.AllItemsCount);
                ClassicAssert.AreEqual(1, testform.UnstagedList.AllItemsCount);
                ClassicAssert.IsTrue(fileNotMatchedByFilterIsStillUnstaged);
            });
        }

        [SetCulture("en-US")]
        [SetUICulture("en-US")]
        [Test]
        public void Should_unstage_only_filtered_on_UnstageAll()
        {
            _referenceRepository.CreateRepoFile("file1A-Привет.txt", "Test");   // escaped and not escaped in the same string
            _referenceRepository.CreateRepoFile("file1B-두다.txt", "Test");      // escaped octal code points (Korean Hangul in this case)
            _referenceRepository.CreateRepoFile("file2.txt", "Test");

            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                ClassicAssert.AreEqual("Unstage all", form.GetTestAccessor().UnstageAllToolItem.ToolTipText);
            });

            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                FormCommit.TestAccessor testform = form.GetTestAccessor();

                ClassicAssert.AreEqual(0, testform.StagedList.AllItemsCount);
                ClassicAssert.AreEqual(3, testform.UnstagedList.AllItemsCount);

                testform.StagedList.SetFilter("");
                testform.StageAllToolItem.PerformClick();

                ClassicAssert.AreEqual(3, testform.StagedList.AllItemsCount);
                ClassicAssert.AreEqual(0, testform.UnstagedList.AllItemsCount);

                testform.StagedList.ClearSelected();
                testform.StagedList.SetFilter("file1");

                ClassicAssert.AreEqual("Unstage filtered", testform.UnstageAllToolItem.ToolTipText);

                testform.UnstageAllToolItem.PerformClick();

                bool fileNotMatchedByFilterIsStillStaged = testform.StagedList.AllItems.Any(i => i.Item.Name == "file2.txt");

                ClassicAssert.AreEqual(2, testform.UnstagedList.AllItemsCount);
                ClassicAssert.AreEqual(1, testform.StagedList.AllItemsCount);
                ClassicAssert.IsTrue(fileNotMatchedByFilterIsStillStaged);
            });
        }

        [Test, TestCaseSource(typeof(CommitMessageTestData), nameof(CommitMessageTestData.TestCases))]
        public void AddSelectionToCommitMessage_shall_be_ignored_unless_diff_is_focused(
            string message,
            int selectionStart,
            int selectionLength,
            string expectedMessage,
            int expectedSelectionStart)
        {
            TestAddSelectionToCommitMessage(focusSelectedDiff: false, CommitMessageTestData.SelectedText,
                message, selectionStart, selectionLength,
                expectedResult: false, expectedMessage: message, expectedSelectionStart: selectionStart);
        }

        [Test, TestCaseSource(typeof(CommitMessageTestData), nameof(CommitMessageTestData.TestCases))]
        public void AddSelectionToCommitMessage_shall_be_ignored_if_no_difftext_is_selected(
            string message,
            int selectionStart,
            int selectionLength,
            string expectedMessage,
            int expectedSelectionStart)
        {
            TestAddSelectionToCommitMessage(focusSelectedDiff: true, selectedText: "",
                message, selectionStart, selectionLength,
                expectedResult: false, expectedMessage: message, expectedSelectionStart: selectionStart);
        }

        [Test, TestCaseSource(typeof(CommitMessageTestData), nameof(CommitMessageTestData.TestCases))]
        public void AddSelectionToCommitMessage_shall_modify_the_commit_message(
            string message,
            int selectionStart,
            int selectionLength,
            string expectedMessage,
            int expectedSelectionStart)
        {
            TestAddSelectionToCommitMessage(focusSelectedDiff: true, CommitMessageTestData.SelectedText,
                message, selectionStart, selectionLength,
                expectedResult: true, expectedMessage, expectedSelectionStart);
        }

        [Test]
        public void EditFileToolStripMenuItem_Click_no_selection_should_not_throw()
        {
            RunFormTest(async form =>
            {
                await AsyncTestHelper.JoinPendingOperationsAsync(AsyncTestHelper.UnexpectedTimeout);

                form.GetTestAccessor().UnstagedList.ClearSelected();

                ToolStripMenuItem editFileToolStripMenuItem = form.GetTestAccessor().EditFileToolStripMenuItem;

                // asserting by the virtue of not crashing
                editFileToolStripMenuItem.PerformClick();
            });
        }

        [Test]
        public void ResetAuthor_depends_on_amend()
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor testForm = form.GetTestAccessor();

                // check initial state
                ClassicAssert.False(testForm.Amend.Checked);
                ClassicAssert.False(testForm.ResetAuthor.Checked);
                ClassicAssert.False(testForm.ResetAuthor.Visible);

                testForm.Amend.Checked = true;

                // check that reset author checkbox becomes visible when amend is checked
                ClassicAssert.True(testForm.Amend.Checked);
                ClassicAssert.True(testForm.ResetAuthor.Visible);

                testForm.ResetAuthor.Checked = true;

                ClassicAssert.True(testForm.Amend.Checked);

                testForm.Amend.Checked = false;

                // check that reset author checkbox becomes invisible and unchecked when amend is unchecked
                ClassicAssert.False(testForm.Amend.Checked);
                ClassicAssert.False(testForm.ResetAuthor.Checked);
                ClassicAssert.False(testForm.ResetAuthor.Visible);

                testForm.Amend.Checked = true;

                // check that when amend is checked again reset author is still unchecked
                ClassicAssert.True(testForm.Amend.Checked);
                ClassicAssert.True(testForm.ResetAuthor.Visible);
                ClassicAssert.False(testForm.ResetAuthor.Checked);
            });
        }

        [Test]
        public void ResetSoft()
        {
            AppSettings.CommitAndPushForcedWhenAmend = true;
            AppSettings.DontConfirmAmend = true;
            AppSettings.CloseCommitDialogAfterCommit = false;
            AppSettings.CloseCommitDialogAfterLastCommit = false;
            AppSettings.CloseProcessDialog = true;

            int defaultBackColor = SystemColors.ButtonFace.ToArgb();
            int forceBackColor = OtherColors.AmendButtonForcedColor.ToArgb();

            const string originalCommitMessage = "commit to be amended by reset soft";
            const string amendedCommitMessage = "replacement commit";

            ObjectId? previousCommitId = _commands.Module.RevParse("HEAD");
            string originalCommitHash = _referenceRepository.CreateCommit(originalCommitMessage, "original content", "theFile");

            RunFormTest(form =>
            {
                FormCommit.TestAccessor ta = form.GetTestAccessor();

                // initial state
                ta.Amend.Enabled.Should().BeTrue();
                ta.Amend.Checked.Should().BeFalse();
                ta.CommitAndPush.BackColor.ToArgb().Should().Be(defaultBackColor);
                ta.ResetSoft.Visible.Should().BeFalse();
                ta.Message.Text.Should().BeEmpty();

                // amend needs to be activated first
                ta.Amend.Checked = true;

                ta.Amend.Enabled.Should().BeTrue();
                ta.Amend.Checked.Should().BeTrue();
                ta.CommitAndPush.BackColor.ToArgb().Should().Be(forceBackColor);
                ta.ResetSoft.Visible.Should().BeTrue();
                ta.ResetSoft.Enabled.Should().BeTrue();
                ta.Message.Text.Should().Be(originalCommitMessage);

                // reset soft
                ta.Message.Text = amendedCommitMessage;
                ta.ResetSoft.PerformClick();

                // update the form
                Application.DoEvents();
                AsyncTestHelper.JoinPendingOperations();

                _commands.Module.RevParse("HEAD").Should().Be(previousCommitId);
                ta.Amend.Enabled.Should().BeFalse();
                ta.Amend.Checked.Should().BeFalse();
                ta.CommitAndPush.BackColor.ToArgb().Should().Be(forceBackColor);
                ta.CommitAndPush.Text.Should().Be(ta.CommitAndForcePushText);
                ta.ResetSoft.Visible.Should().BeFalse();
                ta.Message.Text.Should().Be(amendedCommitMessage);

                // commit
                ta.Commit.PerformClick();

                // update the form
                Application.DoEvents();
                AsyncTestHelper.JoinPendingOperations();

                ta.Amend.Enabled.Should().BeTrue();
                ta.Amend.Checked.Should().BeFalse();
                ta.CommitAndPush.BackColor.ToArgb().Should().Be(forceBackColor);
                ta.CommitAndPush.Text.Should().Be(TranslatedStrings.ButtonPush);
                ta.ResetSoft.Visible.Should().BeFalse();
                ta.Message.Text.Should().BeEmpty();
            });
        }

        [Test]
        public void Dialog_remembers_window_geometry()
        {
            RunGeometryMemoryTest(
                form => form.GetTestAccessor().Bounds,
                (bounds1, bounds2) => bounds2.Should().Be(bounds1));
        }

        [Test]
        public void MessageEdit_remembers_geometry()
        {
            RunGeometryMemoryTest(
                form => form.GetTestAccessor().Message.Bounds,
                (bounds1, bounds2) => bounds2.Should().Be(bounds1));
        }

        [Test]
        public void UnstagedList_remembers_geometry()
        {
            RunGeometryMemoryTest(
                form => form.GetTestAccessor().UnstagedList.Bounds,
                (bounds1, bounds2) =>
                {
                    bounds2.Width.Should().Be(bounds1.Width);

                    // The method to determine the height is prone to rounding errors.
                    // This seems not to affect the user experience, because
                    // - the rounding error is only +- 1 pixel
                    // - if the user does not change the geometry, the height will oscillate to a constant value
                    int height1 = bounds1.Height;
                    int height2 = bounds2.Height;
                    ClassicAssert.IsTrue(height1 >= height2 - 1 && height1 <= height2 + 1);
                });
        }

        [Test]
        public void SelectedDiff_remembers_geometry()
        {
            RunGeometryMemoryTest(
                form => form.GetTestAccessor().SelectedDiff.Bounds,
                (bounds1, bounds2) => bounds2.Should().Be(bounds1));
        }

        [TestCase("", 0, "feat: ", 6)]
        [TestCase("text", 3, "feat: text", 9)]
        public void ConventionalCommit_keyword_is_prefixed_when_none(string initialText, int initialPosition,
            string expectedText, int expectedPosition)
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor testForm = form.GetTestAccessor();
                testForm.SetMessageState(initialText, initialPosition);
                testForm.IncludeFeatureParentheses = false;
                (string message, int selectionStart) = testForm.PrefixOrReplaceKeyword("feat");
                ClassicAssert.AreEqual(expectedText, message);
                ClassicAssert.AreEqual(expectedPosition, selectionStart);
            });
        }

        [TestCase("", 0, "feat(): ", 5)]
        [TestCase("text", 3, "feat(): text", 5)]
        public void ConventionalCommit_keyword_is_prefixed_when_none_with_scope(string initialText, int initialPosition,
            string expectedText, int expectedPosition)
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor testForm = form.GetTestAccessor();
                testForm.SetMessageState(initialText, initialPosition);
                testForm.IncludeFeatureParentheses = true;
                (string message, int selectionStart) = testForm.PrefixOrReplaceKeyword("feat");
                ClassicAssert.AreEqual(expectedText, message);
                ClassicAssert.AreEqual(expectedPosition, selectionStart);
            });
        }

        [TestCase("fix: ", 0, "feat: ", 6)]
        [TestCase("fix: text", 3, "feat: text", 6)]
        public void ConventionalCommit_keyword_is_prefixed_when_already_typed(string initialText, int initialPosition,
            string expectedText, int expectedPosition)
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor testForm = form.GetTestAccessor();
                testForm.SetMessageState(initialText, initialPosition);
                testForm.IncludeFeatureParentheses = false;
                (string message, int selectionStart) = testForm.PrefixOrReplaceKeyword("feat");
                ClassicAssert.AreEqual(expectedText, message);
                ClassicAssert.AreEqual(expectedPosition, selectionStart);
            });
        }

        [TestCase("fix: ", 0, "feat(): ", 5)]
        [TestCase("fix: text", 3, "feat(): text", 5)]
        [TestCase("fix(scope): ", 0, "feat(scope): ", 13)]
        [TestCase("fix(scope): text", 14, "feat(scope): text", 15)]
        public void ConventionalCommit_keyword_is_prefixed_when_already_typed_with_scope(string initialText, int initialPosition,
            string expectedText, int expectedPosition)
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor testForm = form.GetTestAccessor();
                testForm.SetMessageState(initialText, initialPosition);
                testForm.IncludeFeatureParentheses = true;
                (string message, int selectionStart) = testForm.PrefixOrReplaceKeyword("feat");
                ClassicAssert.AreEqual(expectedText, message);
                ClassicAssert.AreEqual(expectedPosition, selectionStart);
            });
        }

        [Test]
        public void MainSplitter_Remembers_Distance()
        {
            bool splitterMoved = false;
            RunGeometryMemoryTest(
                form =>
                {
                    if (!splitterMoved)
                    {
                        form.GetTestAccessor().MainSplitter.SplitterDistance += 100;
                        splitterMoved = true;
                    }

                    return form.GetTestAccessor().UnstagedList.Bounds;
                },
                (bounds1, bounds2) => bounds2.Should().Be(bounds1));
        }

        private void TestAddSelectionToCommitMessage(
            bool focusSelectedDiff,
            string selectedText,
            string message,
            int selectionStart,
            int selectionLength,
            bool expectedResult,
            string expectedMessage,
            int expectedSelectionStart)
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor ta = form.GetTestAccessor();

                FileViewerInternal selectedDiff = ta.SelectedDiff.GetTestAccessor().FileViewerInternal;
                selectedDiff.SetText(selectedText, openWithDifftool: null);
                selectedDiff.GetTestAccessor().TextEditor.ActiveTextAreaControl.SelectionManager.SetSelection(
                    new TextLocation(0, 0), new TextLocation(selectedText.Length, 0));
                if (focusSelectedDiff)
                {
                    selectedDiff.Focus();
                }

                ta.Message.Text = message;
                ta.Message.SelectionStart = selectionStart;
                ta.Message.SelectionLength = selectionLength;
                ta.ExecuteCommand(FormCommit.Command.AddSelectionToCommitMessage).Should().Be(expectedResult);
                ta.Message.Text.Should().Be(expectedMessage);
                ta.Message.SelectionStart.Should().Be(expectedSelectionStart);
                ta.Message.SelectionLength.Should().Be(expectedResult ? 0 : selectionLength);
            });
        }

        [Test]
        public void ShouldNotUndoRenameFileWhenResettingStagedLines()
        {
            RunFormTest(form =>
            {
                FormCommit.TestAccessor ta = form.GetTestAccessor();

                FileViewer.TestAccessor selectedDiff = ta.SelectedDiff.GetTestAccessor();
                FileViewerInternal? selectedDiffInternal = selectedDiff.FileViewerInternal;

                // Commit a file, rename it and introduce a slight content change
                string contents = "this\nhas\nmany\nlines\nthis\nhas\nmany\nlines\nthis\nhas\nmany\nlines?\n";
                _referenceRepository.CreateCommit("commit", contents, "original.txt");
                _referenceRepository.DeleteRepoFile("original.txt");
                contents = contents.Replace("?", "!");
                _referenceRepository.CreateRepoFile("original2.txt", contents);

                ta.RescanChanges();
                AsyncTestHelper.JoinPendingOperations();

                ta.UnstagedList.SelectedItems = ta.UnstagedList.AllItems;
                ta.UnstagedList.Focus();
                ta.ExecuteCommand(RevisionDiffControl.Command.StageSelectedFile);

                ta.StagedList.SelectedGitItem = ta.StagedList.AllItems.Single(i => i.Item.Name.Contains("original2.txt")).Item;

                selectedDiffInternal.Focus();
                AsyncTestHelper.JoinPendingOperations();

                selectedDiffInternal.GetTestAccessor().TextEditor.ActiveTextAreaControl.SelectionManager.SetSelection(
                    new TextLocation(2, 11), new TextLocation(5, 12));

                int textLengthBeforeReset = selectedDiffInternal.GetTestAccessor().TextEditor.ActiveTextAreaControl.Document.TextLength;

                selectedDiff.ResetSelectedLinesConfirmationDialog.Created += (s, e) =>
                {
                    // Auto-press `Yes`
                    selectedDiff.ResetSelectedLinesConfirmationDialog.Buttons[0].PerformClick();
                };
                selectedDiff.ExecuteCommand(FileViewer.Command.ResetLines);

                ta.RescanChanges();
                AsyncTestHelper.JoinPendingOperations();

                int textLengthAfterReset = selectedDiffInternal.GetTestAccessor().TextEditor.ActiveTextAreaControl.Document.TextLength;

                textLengthBeforeReset.Should().BeGreaterThan(0);
                textLengthAfterReset.Should().BeGreaterThan(0);
                textLengthAfterReset.Should().BeLessThan(textLengthBeforeReset);
                FileStatusItem? stagedAndRenamed = ta.StagedList.AllItems.FirstOrDefault(i => i.Item.Name.Contains("original2.txt"));
                stagedAndRenamed.Should().NotBeNull();
                stagedAndRenamed!.Item.IsRenamed.Should().BeTrue();
            });
        }

        private void RunGeometryMemoryTest(Func<FormCommit, Rectangle> boundsAccessor, Action<Rectangle, Rectangle> testDriver)
        {
            Rectangle bounds1 = Rectangle.Empty;
            Rectangle bounds2 = Rectangle.Empty;
            RunFormTest(form => bounds1 = boundsAccessor(form));
            RunFormTest(form => bounds2 = boundsAccessor(form));
            testDriver(bounds1, bounds2);
        }

        private void RunFormTest(Action<FormCommit> testDriver, CommitKind commitKind = CommitKind.Normal)
        {
            RunFormTest(
                form =>
                {
                    testDriver(form);
                    return Task.CompletedTask;
                },
                commitKind);
        }

        private void RunFormTest(Func<FormCommit, Task> testDriverAsync, CommitKind commitKind = CommitKind.Normal)
        {
            UITest.RunForm(
                showForm: () =>
                {
                    ClassicAssert.True(commitKind switch
                    {
                        CommitKind.Normal => _commands.StartCommitDialog(owner: null),
                        CommitKind.Squash => _commands.StartSquashCommitDialog(owner: null, _referenceRepository.Module.GetRevision()),
                        CommitKind.Fixup => _commands.StartFixupCommitDialog(owner: null, _referenceRepository.Module.GetRevision()),
                        CommitKind.Amend => _commands.StartAmendCommitDialog(owner: null, _referenceRepository.Module.GetRevision()),
                        _ => throw new ArgumentException($"Unsupported commit kind: {commitKind}", nameof(commitKind))
                    });

                    // Await updated FileViewer
                    AsyncTestHelper.JoinPendingOperations();
                },
                testDriverAsync);
        }
    }

    public class CommitMessageTestData
    {
        internal const string SelectedText = "selection";
        private const string SelectedTextWithNewLine = SelectedText + "\n";

        public static IEnumerable TestCases
        {
            get
            {
                yield return new TestCaseData("", 0, 0, SelectedTextWithNewLine, 0 + SelectedTextWithNewLine.Length);
                yield return new TestCaseData("msg", 0, 0, SelectedTextWithNewLine + "msg", 0 + SelectedTextWithNewLine.Length);
                yield return new TestCaseData("msg", 1, 0, "m" + SelectedTextWithNewLine + "sg", 1 + SelectedTextWithNewLine.Length);
                yield return new TestCaseData("msg", 2, 0, "ms" + SelectedTextWithNewLine + "g", 2 + SelectedTextWithNewLine.Length);
                yield return new TestCaseData("msg", 3, 0, "msg" + SelectedTextWithNewLine, 3 + SelectedTextWithNewLine.Length);
                yield return new TestCaseData("msg", 0, 1, "" + SelectedText + "sg", 0 + SelectedText.Length);
                yield return new TestCaseData("msg", 1, 1, "m" + SelectedText + "g", 1 + SelectedText.Length);
                yield return new TestCaseData("msg", 2, 1, "ms" + SelectedText, 2 + SelectedText.Length);
            }
        }
    }
}
