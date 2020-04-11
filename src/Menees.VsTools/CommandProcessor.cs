﻿namespace Menees.VsTools
{
	#region Using Directives

	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Diagnostics.CodeAnalysis;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Runtime.InteropServices;
	using System.Text;
	using System.Threading.Tasks;
	using EnvDTE;
	using Microsoft.VisualStudio;
	using Microsoft.VisualStudio.Shell;
	using Microsoft.VisualStudio.Shell.Interop;
	using VSLangProj;

	#endregion

	internal sealed class CommandProcessor
	{
		#region Private Data Members

		private readonly MainPackage package;
		private readonly DTE dte;

		#endregion

		#region Constructors

		public CommandProcessor(MainPackage package)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			this.package = package;
			this.dte = (DTE)this.GetService(typeof(SDTE));
		}

		#endregion

		#region Private Properties

		private Language ActiveLanguage
		{
			get
			{
				ThreadHelper.ThrowIfNotOnUIThread();
				Language result = Utilities.GetLanguage(this.dte.ActiveDocument);
				return result;
			}
		}

		#endregion

		#region Public Methods

		public bool CanExecute(Command command)
		{
			try
			{
				ThreadHelper.ThrowIfNotOnUIThread();
				bool result = false;

				if (this.dte != null)
				{
					switch (command)
					{
						// These require a text selection.
						case Command.SortLines:
						case Command.Trim:
						case Command.Statistics:
						case Command.ExecuteText:
							result = new TextDocumentHandler(this.dte).HasNonEmptySelection;
							break;

						// These require a text selection for specific languages.
						case Command.CommentSelection:
						case Command.UncommentSelection:
							result = CommentHandler.CanCommentSelection(this.dte, command == Command.CommentSelection);
							break;

						// These require a document using a supported language.
						case Command.AddRegion:
						case Command.CollapseAllRegions:
						case Command.ExpandAllRegions:
							result = RegionHandler.IsSupportedLanguage(this.ActiveLanguage);
							break;

						// These require an open document with a backing file on disk.
						case Command.ExecuteFile:
							string fileName = this.GetDocumentFileName();
							result = File.Exists(fileName);
							break;

						case Command.GenerateGuid:
							result = new TextDocumentHandler(this.dte).CanSetSelectedText;
							break;

						case Command.AddToDoComment:
							result = CommentHandler.CanAddToDoComment(this.dte);
							break;
					}
				}

				return result;
			}
			catch (Exception ex)
			{
				MainPackage.LogException(ex);
				throw;
			}
		}

		public void Execute(Command command)
		{
			try
			{
				ThreadHelper.ThrowIfNotOnUIThread();
				if (this.dte != null)
				{
					switch (command)
					{
						case Command.AddRegion:
							RegionHandler.AddRegion(this.dte, Options.SplitValues(this.package.Options.PredefinedRegions));
							break;

						case Command.CollapseAllRegions:
							RegionHandler.CollapseAllRegions(this.dte, this.ActiveLanguage, this.package);
							break;

						case Command.CommentSelection:
						case Command.UncommentSelection:
							CommentHandler.CommentSelection(this.dte, this.package, command == Command.CommentSelection);
							break;

						case Command.ExecuteFile:
							this.ExecuteFile();
							break;

						case Command.ExecuteText:
							this.ExecuteText();
							break;

						case Command.ExpandAllRegions:
							RegionHandler.ExpandAllRegions(this.dte, this.ActiveLanguage);
							break;

						case Command.GenerateGuid:
							this.GenerateGuid();
							break;

						case Command.SortLines:
							this.SortLines();
							break;

						case Command.Statistics:
							this.Statistics();
							break;

						case Command.Trim:
							this.Trim();
							break;

						case Command.AddToDoComment:
							CommentHandler.AddToDoComment(this.dte);
							break;
					}
				}
			}
			catch (Exception ex)
			{
				MainPackage.LogException(ex);
				throw;
			}
		}

		#endregion

		#region Private Methods

		private object GetService(Type serviceType)
		{
			object result = this.package.ServiceProvider.GetService(serviceType);
			return result;
		}

		private string GetDocumentFileName()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			Document doc = this.dte.ActiveDocument;
			string result = doc?.FullName;
			return result;
		}

		private void ExecuteFile()
		{
			// Perform the SaveAll first (if necessary) in case the "executing" file has never
			// been saved before, so this will assign it a file name.
			ThreadHelper.ThrowIfNotOnUIThread();
			bool performExecute = true;
			Documents allDocs = this.dte.Documents;
			if (allDocs != null && this.package.Options.SaveAllBeforeExecuteFile)
			{
				try
				{
					allDocs.SaveAll();
				}
				catch (ExternalException ex)
				{
					performExecute = false;

					// Rethrow the exception unless the user hit Cancel when prompted to save changes for a document.
					// Cancelling throws an HRESULT of 0x80004004, which is the C constant E_ABORT with the description
					// "Operation aborted".
					const uint E_ABORT = 0x80004004;
					if (unchecked((uint)ex.ErrorCode) != E_ABORT)
					{
						throw;
					}
				}
			}

			if (performExecute)
			{
				string fileName = this.GetDocumentFileName();
				if (!string.IsNullOrEmpty(fileName))
				{
					Utilities.ShellExecute(fileName);
				}
			}
		}

		private void ExecuteText()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			TextDocumentHandler handler = new TextDocumentHandler(this.dte);
			if (handler.HasNonEmptySelection)
			{
				Utilities.ShellExecute(handler.SelectedText);
			}
		}

		private void GenerateGuid()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			TextDocumentHandler handler = new TextDocumentHandler(this.dte);
			if (handler.CanSetSelectedText)
			{
				Guid guid = Guid.NewGuid();
				Options options = this.package.Options;

				string format;
				switch (options.GuidFormat)
				{
					case GuidFormat.Numbers:
						format = "N";
						break;

					case GuidFormat.Braces:
						format = "B";
						break;

					case GuidFormat.Parentheses:
						format = "P";
						break;

					case GuidFormat.Structure:
						format = "X";
						break;

					default: // GuidFormat.Dashes
						format = "D";
						break;
				}

				string guidText = guid.ToString(format);
				if (options.UppercaseGuids)
				{
					guidText = guidText.ToUpper();
				}

				// Set the selection to the new GUID
				handler.SetSelectedText(guidText, "Generate GUID");
			}
		}

		private void SortLines()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			TextDocumentHandler handler = new TextDocumentHandler(this.dte);
			if (handler.HasNonEmptySelection)
			{
				Options options = this.package.Options;

				SortDialog dialog = new SortDialog();
				if (dialog.Execute(options))
				{
					StringComparison comparison;
					if (options.SortCompareByOrdinal)
					{
						comparison = options.SortCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
					}
					else
					{
						comparison = options.SortCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
					}

					// Now sort the lines and put them back as the selection
					string text = handler.SelectedText;
					TextLines lines = new TextLines(text);
					lines.Sort(comparison, options.SortAscending, options.SortIgnoreWhitespace, options.SortIgnorePunctuation, options.SortEliminateDuplicates);
					string sortedText = lines.ToString();
					handler.SetSelectedTextIfUnchanged(sortedText, "Sort Lines");
				}
			}
		}

		private void Statistics()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			TextDocumentHandler handler = new TextDocumentHandler(this.dte);
			if (handler.HasNonEmptySelection)
			{
				string text = handler.SelectedText;
				StatisticsDialog dialog = new StatisticsDialog();
				dialog.Execute(text);
			}
		}

		private void Trim()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			TextDocumentHandler handler = new TextDocumentHandler(this.dte);
			if (handler.HasNonEmptySelection)
			{
				Options options = this.package.Options;

				bool execute = true;
				if (!options.OnlyShowTrimDialogWhenShiftIsPressed || Utilities.IsShiftPressed)
				{
					TrimDialog dialog = new TrimDialog();
					execute = dialog.Execute(options);
				}

				if (execute && (options.TrimStart || options.TrimEnd))
				{
					string text = handler.SelectedText;
					TextLines lines = new TextLines(text);
					lines.Trim(options.TrimStart, options.TrimEnd);
					string trimmedText = lines.ToString();
					handler.SetSelectedTextIfUnchanged(trimmedText, nameof(this.Trim));
				}
			}
		}

		#endregion
	}
}
