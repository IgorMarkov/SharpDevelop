// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Siegfried Pammer" email="sie_pam@gmx.at"/>
//     <version>$Revision: 3731 $</version>
// </file>

using ICSharpCode.SharpDevelop.Editor.CodeCompletion;
using System;
using System.Linq;
using ICSharpCode.NRefactory.Ast;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Dom.Refactoring;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.XmlEditor;

namespace ICSharpCode.XamlBinding
{
	sealed class XamlCompletionItemList : DefaultCompletionItemList
	{
		public XamlCompletionItemList()
		{
		}
		
		public override CompletionItemListKeyResult ProcessInput(char key)
		{
			if (key == ':' || key == '/')
				return CompletionItemListKeyResult.NormalKey;
			
			return base.ProcessInput(key);
		}
		
		public override void Complete(CompletionContext context, ICompletionItem item)
		{
			base.Complete(context, item);
			
			if (item is XamlCodeCompletionItem) {
				XamlCodeCompletionItem cItem = item as XamlCodeCompletionItem;
				
				if (cItem.Entity is IProperty || cItem.Entity is IEvent) {
					if (context.Editor.Document.GetCharAt(context.StartOffset - 1) != '.') {
						if (!item.Text.EndsWith("=", StringComparison.OrdinalIgnoreCase)) {
							context.Editor.Document.Insert(context.EndOffset, "=\"\"");
							context.Editor.Caret.Offset--;
						} else {
							XamlCompletionContext xamlContext = CompletionDataHelper.ResolveCompletionContext(context.Editor, context.CompletionChar);
							if (!string.IsNullOrEmpty(xamlContext.RawAttributeValue)) {
								string valuePart = xamlContext.RawAttributeValue.Substring(0, xamlContext.ValueStartOffset);
								AttributeValue value = MarkupExtensionParser.ParseValue(valuePart);
								
								if (value != null && !value.IsString) {
									var markup = Utils.GetInnermostMarkup(value.ExtensionValue);
									if (markup.NamedArguments.Count > 0 || markup.PositionalArguments.Count > 0) {
										int oldOffset = context.Editor.Caret.Offset;
										context.Editor.Caret.Offset = context.StartOffset;
										string word = context.Editor.GetWordBeforeCaret().Trim();
										
										if (!word.EndsWith(",") && markup.ExtensionType != word) {
											context.Editor.Document.Insert(context.Editor.Caret.Offset, ", ");
											oldOffset += 2;
										}
										
										context.Editor.Caret.Offset = oldOffset;
									}
								}
							}
						}
						
						XamlCodeCompletionBinding.Instance.CtrlSpace(context.Editor);
					}
				}
				
				if (cItem.Entity is IClass) {
					IClass c = cItem.Entity as IClass;
					if (c.FullyQualifiedName == "System.Windows.Style") {
						string insertionString = "";
						if (!char.IsWhiteSpace(context.Editor.Document.GetCharAt(context.StartOffset - 1))) {
							insertionString = " ";
						}
						
						string prefix = Utils.GetXamlNamespacePrefix(CompletionDataHelper.ResolveCompletionContext(context.Editor, context.CompletionChar));
						if (!string.IsNullOrEmpty(prefix))
							prefix += ":";
						
						insertionString += "TargetType=\"{" + prefix + "Type }\"";
						context.Editor.Document.Insert(context.EndOffset, insertionString);
						context.Editor.Caret.Offset = context.EndOffset + insertionString.Length - 2;
						
						XamlCodeCompletionBinding.Instance.CtrlSpace(context.Editor);
					} else if (c.FullyQualifiedName == "System.Windows.Setter") {
						string insertionString = "";
						if (!char.IsWhiteSpace(context.Editor.Document.GetCharAt(context.StartOffset - 1))) {
							insertionString = " ";
						}
						insertionString += "Property=\"\"";
						context.Editor.Document.Insert(context.EndOffset, insertionString);
						context.Editor.Caret.Offset = context.EndOffset + insertionString.Length - 1;
						
						XamlCodeCompletionBinding.Instance.CtrlSpace(context.Editor);
					}
				}
			}
			
			if (item is NewEventCompletionItem) {
				NewEventCompletionItem eventItem = item as NewEventCompletionItem;
				CreateEventHandlerCode(context, eventItem);
			}
			
			if (item is XmlnsCompletionItem) {
				context.Editor.Caret.Offset++;
			}
			
			if (item is XamlCompletionItem) {
				XamlCompletionItem xamlItem = item as XamlCompletionItem;
				context.Editor.Document.Insert(context.EndOffset, "=\"\"");
				context.Editor.Caret.Offset--;
			}
		}
		
		static void CreateEventHandlerCode(CompletionContext context, NewEventCompletionItem completionItem)
		{
			ParseInformation p = ParserService.GetParseInformation(context.Editor.FileName);
			var unit = p.MostRecentCompilationUnit;
			var loc = context.Editor.Document.OffsetToPosition(context.StartOffset);
			IClass c = unit.GetInnermostClass(loc.Line, loc.Column);
			if (c == null)
				return;
			IMethod initializeComponent = c.Methods[0];
			CompoundClass compound = c.GetCompoundClass() as CompoundClass;
			if (compound != null) {
				foreach (IClass part in compound.Parts) {
					IMember lastMember = part.Methods.LastOrDefault();
					
					if (lastMember != null && lastMember.ToString() == initializeComponent.ToString())
						continue;
					
					if (completionItem.EventType.ReturnType == null)
						return;
					
					IMethod method = completionItem.EventType.ReturnType.GetMethods().FirstOrDefault(m => m.Name == "Invoke");
					
					if (method == null)
						throw new ArgumentException("delegateType is not a valid delegate!");
					
					ParametrizedNode node = CodeGenerator.ConvertMember(method, new ClassFinder(part, context.Editor.Caret.Line, context.Editor.Caret.Column));
					
					node.Name = completionItem.HandlerName;
					
					node.Modifier = Modifiers.None;
					
					IViewContent viewContent = FileService.OpenFile(part.CompilationUnit.FileName);
					IFileDocumentProvider document = viewContent as IFileDocumentProvider;
					
					if (viewContent != null || document != null) {
						if (lastMember != null)
							unit.ProjectContent.Language.CodeGenerator.InsertCodeAfter(lastMember, new RefactoringDocumentAdapter(document.GetDocumentForFile(viewContent.PrimaryFile)), node);
						else
							unit.ProjectContent.Language.CodeGenerator.InsertCodeAtEnd(part.Region, new RefactoringDocumentAdapter(document.GetDocumentForFile(viewContent.PrimaryFile)), node);
					}
					return;
				}
			}
		}
	}
}