﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using ICSharpCode.AvalonEdit;
using System.Windows.Input;
using XSemmel.Configuration;
using XSemmel.Helpers;
using XSemmel.Schema.Parser;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace XSemmel.Editor
{
    class CodeCompletion
    {
        private readonly TextEditor _editor;

        private SchemaParser _schemaParser;

        private CompletionWindow _completionWindow;


        public CodeCompletion(TextEditor editor)
        {
            _editor = editor;

            if (XSConfiguration.Instance.Config.EnableCodeCompletion)
            {
                _editor.TextArea.TextEntering += textEditor_TextArea_TextEntering;
                _editor.TextArea.TextEntered += textEditor_TextArea_TextEntered;
                _editor.TextArea.KeyDown += textEditor_TextArea_KeyDown;
            }
        }


        private void textEditor_TextArea_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var offset = _editor.CaretOffset;
                if (offset > 0)
                {
                    char c = _editor.Text[offset - 1];

                    TextComposition tc = new TextComposition(null, null, c.ToString(CultureInfo.InvariantCulture));
                    var tcea = new TextCompositionEventArgs(null, tc);

                    bool foundProposal = completeBasedOnTextEntered(tcea);
                    if (!foundProposal)
                    {
                        completeOnCtrlSpace();
                    }
                }
                e.Handled = true;
            }
        }

        private void textEditor_TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            completeBasedOnTextEntered(e);
        }

        private bool completeOnCtrlSpace()
        {
            if (XParser.IsInsideComment(_editor.Text, _editor.CaretOffset))
            {
                IList<ICompletionData> data = new List<ICompletionData>();
                data.Add(new MyCompletionData("-->"));
                showCompletion(data);
                return true;
            }

            if (XParser.IsInsideEmptyElement(_editor.Text, _editor.CaretOffset))
            {
                //replace <test/> --> <test></test>

                IList<ICompletionData> data = new List<ICompletionData>();

//                int idxStart = _editor.Text.LastIndexOf('<', _editor.CaretOffset);
//                int idxEnd = _editor.Text.IndexOf('>', _editor.CaretOffset);
//                Debug.Assert(idxStart !=-1 && idxEnd != -1, "Can only happen of XParser.IsInsideEmptyElement is incorrect");
//
//                if (idxStart >= 0 && idxEnd > idxStart)
//                {
                    data.Add(new ActionCompletionData("Expand empty tag", null, (textArea, completionSegment, eventArgs) =>
                        {
                            int newCursor;
                            int startReplace;
                            int lengthReplace;
                            string replaceWith;
                            XActor.ExpandEmptyTag(_editor.Text, _editor.CaretOffset, out newCursor, out startReplace,
                                out lengthReplace, out replaceWith);
        
                            textArea.Document.Replace(startReplace, lengthReplace, replaceWith);
                            _editor.CaretOffset = newCursor;

//                            string tag = XParser.GetElementAtCursor(_editor.Text, _editor.CaretOffset - 1);
//                            string element = string.Format("<{0}></{0}>", tag);
//                            textArea.Document.Replace(idxStart, idxEnd - idxStart + 1, element);
//                            _editor.CaretOffset = idxStart + tag.Length + 2;
                        }));
//                }

                showCompletion(data);
                return true;
            }
            
            return false;
        }

        private bool completeBasedOnTextEntered(TextCompositionEventArgs e)
        {
            try 
            {
                switch (e.Text)
                {
                    case ">":
                    {
                        //auto-insert closing element
                        int offset = _editor.CaretOffset;
                        string s = XParser.GetElementAtCursor(_editor.Text, offset - 1);
                        if (!string.IsNullOrWhiteSpace(s) && "!--"!=s)
                        {
                            if (!XParser.IsClosingElement(_editor.Text, offset-1, s))
                            {
                                _editor.TextArea.Document.Insert(offset, "</" + s + ">");
                                _editor.CaretOffset = offset;
                                return true;
                            }
                        }
                        break;
                    }
                    case "/":
                    {
                        //insert name of closing element
                        int offset = _editor.CaretOffset;
                        if (offset > 1 &&_editor.Text[offset-2] == '<')
                        {
                            //expand to closing tag
                            string s = XParser.GetParentElementAtCursor(_editor.Text, offset - 1);
                            if (!string.IsNullOrEmpty(s))
                            {
                                showCompletion(new List<ICompletionData>
                                    {
                                        new MyCompletionData(s + ">")
                                    });
                                return true;
                            }
                        }
                        if (_editor.Text.Length > offset + 2 && _editor.Text[offset] == '>')
                        {
                            //remove closing tag if exist
                            string s = XParser.GetElementAtCursor(_editor.Text, offset - 1);
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                //search closing end tag. Element must be empty (whitespace allowed)  
                                //"<hallo>  </hallo>" --> enter '/' --> "<hallo/>  "
                                string expectedEndTag = "</" + s + ">";
                                for (int i = offset+1; i < _editor.Text.Length - expectedEndTag.Length; i++)
                                {
                                    if (!char.IsWhiteSpace(_editor.Text[i]))
                                    {
                                        if (_editor.Text.Substring(i, expectedEndTag.Length) == expectedEndTag)
                                        {
                                            //remove already existing endTag
                                            _editor.Document.Remove(i, expectedEndTag.Length);
                                        }
                                        break;
                                    }
                                }

                            }
                        }
                        break;
                    }
                    case "!":
                    {
                        int offset = _editor.CaretOffset;
                        if (offset > 1 && _editor.Text[offset - 2] == '<')
                        {
                            _editor.TextArea.Document.Insert(offset, "-- ");
                            _editor.CaretOffset = offset + 2;
                            return true;
                        }
                        break;
                    }
                    case "\"":
                    {
                        //auto-insert closing apostroph
                        int offset = _editor.CaretOffset;
                        int countApostroph = 0;
                        for (int i = offset; i >= 0; i--)
                        {
                            char charAtCursor = _editor.Text[i];
                            if (charAtCursor == '\"')
                            {
                                countApostroph++;
                            }
                            else if (charAtCursor == '<')
                            {
                                break;
                            }
                        }

                        bool oddLeft = (countApostroph%2 == 1);

                        for (int i = offset; i < _editor.Text.Length; i++)
                        {
                            char charAtCursor = _editor.Text[i];
                            if (charAtCursor == '\"')
                            {
                                countApostroph++;
                            }
                            else if (charAtCursor == '>')
                            {
                                break;
                            }
                        }

                        bool oddRight = (countApostroph % 2 == 1);
                        
                        if (oddLeft && oddRight)
                        {
                            _editor.TextArea.Document.Insert(offset, "\"");
                            _editor.CaretOffset = offset;
                            return true;
                        }
                        break;
                    }
                    case " ":
                    {
                        if (_schemaParser == null)
                        {
                            return false;
                        }

                        int offset = _editor.CaretOffset;
                        if (XParser.IsInsideElementDeclaration(_editor.Text, offset - 1))
                        {
                            string element = XParser.GetElementAtCursorFuzzy(_editor.Text, offset - 1);

                            IList<IXsdNode> all = _schemaParser.GetAllNodes();
                            IXsdNode node = getNodeWithName(all, element);
                            if (node != null)
                            {
                                ICollection<XsdAttribute> attrs = getAttributeNames(node);
                                if (attrs != null && attrs.Count > 0)
                                {
                                    IList<ICompletionData> data = new List<ICompletionData>();
                                    foreach (XsdAttribute attr in attrs)
                                    {
                                        if (attr.Annotation != null && attr.Annotation.Count > 0)
                                        {
                                            StringBuilder description = new StringBuilder();
                                            foreach (string ann in attr.Annotation)
                                            {
                                                description.AppendLine(ann);
                                            }
                                            data.Add(new MyCompletionData(attr.Name, attr.Name + "=\"", description.ToString()));
                                        }
                                        else
                                        {
                                            data.Add(new MyCompletionData(attr.Name, attr.Name+"=\"", null));
                                        }
                                    }
                                    showCompletion(data);
                                    return true;
                                }
                            }
                        }

                        break;
                    }
                    case "<":
                    {
                        if (_schemaParser == null)
                        {
                            return false;
                        }
                        int offset = _editor.CaretOffset;
                        string parent = XParser.GetParentElementAtCursor(_editor.Text, offset);

                        XsdElement[] names;
                        if (parent == "")
                        {
                            IXsdNode root = _schemaParser.GetVirtualRoot();
                            names = getChildNames(root);
                        }
                        else
                        {
                            IList<IXsdNode> all = _schemaParser.GetAllNodes();
                            IXsdNode node = getNodeWithName(all, parent);
                            if (node != null)
                            {
                                names = getChildNames(node);
                            }
                            else
                            {
                                names = null;
                            }
                        }

                        if (names != null && names.Length > 0)
                        {
                            IList<ICompletionData> data = new List<ICompletionData>();
                            foreach (XsdElement name in names)
                            {
                                if (name.Annotation != null && name.Annotation.Count > 0)
                                {
                                    StringBuilder sb = new StringBuilder();
                                    foreach (string ann in name.Annotation)
                                    {
                                        sb.AppendLine(ann);
                                    }
                                    data.Add(new MyCompletionData(name.Name, sb.ToString()));
                                }
                                else
                                {
                                    data.Add(new MyCompletionData(name.Name));
                                }
                            }
                            showCompletion(data);
                            return true;
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
            }
            return false;
        }

        private void showCompletion(IEnumerable<ICompletionData> datas)
        {
            _completionWindow = new CompletionWindow(_editor.TextArea);
            IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
            foreach (ICompletionData name in datas)
            {
                data.Add(name);
            }
            _completionWindow.Show();
            _completionWindow.Closed += delegate
            {
                _completionWindow = null;
            };
        }

        private IXsdNode getNodeWithName(IEnumerable<IXsdNode> nodes, string name)
        {
            foreach (IXsdNode xsdNode in nodes)
            {
                if (xsdNode is IXsdHasName)
                {
                    IXsdHasName named = (IXsdHasName)xsdNode;
                    if (named.Name == name)
                    {
                        return xsdNode;
                    }
                }
            }
            return null;
        }

        private ICollection<XsdAttribute> getAttributeNames(IXsdNode node)
        {
            List<XsdAttribute> result = new List<XsdAttribute>();
            if (node is IXsdHasAttribute)
            {
                result.AddRange(((IXsdHasAttribute)node).GetAttributes());
            }
            if (node is IXsdHasType)
            {
                IXsdNode typeTarget = ((IXsdHasType)node).TypeTarget;
                if (typeTarget is IXsdHasAttribute)
                {
                    //don't call me recursively to prevent endless loops
                    result.AddRange(((IXsdHasAttribute)typeTarget).GetAttributes());
                }
            }
            if (node is IXsdHasRef)
            {
                IXsdNode refTarget = ((IXsdHasRef)node).RefTarget;
                if (refTarget is IXsdHasAttribute)
                {
                    //don't call me recursively to prevent endless loops
                    result.AddRange(((IXsdHasAttribute)refTarget).GetAttributes());
                }
            }
            foreach (XsdExtension kid in node.GetChildren().OfType<XsdExtension>())
            {
                result.AddRange(getAttributeNames(kid));
            }
            return result;
        }

        private XsdElement[] getChildNames(IXsdNode node)
        {
            IList<XsdElement> result = new List<XsdElement>();
            getChildNames(node, result);
            return result.ToArray();
        }

        private void getChildNames(IXsdNode node, ICollection<XsdElement> result)
        {
            var kids = node.GetChildren();

            foreach (IXsdNode xsdNode in kids)
            {
                if (xsdNode is XsdElement)
                {
                    result.Add((XsdElement)xsdNode);
                }
                else if (xsdNode is XsdAllSequenceChoice)
                {
                    XsdAllSequenceChoice list = (XsdAllSequenceChoice)xsdNode;
                    getChildNames(list, result);
                }
            }

            if (node is IXsdHasType)
            {
                if (((IXsdHasType)node).TypeTarget != null)
                {
                    IXsdHasType n = (IXsdHasType)node;
                    getChildNames(n.TypeTarget, result);
                }
            }
            if (node is IXsdHasRef)
            {
                if (((IXsdHasRef)node).RefTarget != null)
                {
                    IXsdHasRef n = (IXsdHasRef)node;
                    getChildNames(n.RefTarget, result);
                }
            }
        }

        private void textEditor_TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && _completionWindow != null)
            {
                char c = e.Text[0];
                if (!(char.IsLetterOrDigit(c) || char.IsPunctuation(c) ))  
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    _completionWindow.CompletionList.RequestInsertion(e);
                    e.Handled = true;
                }
            }
            // do not set e.Handled=true - we still want to insert the character that was typed
        }

        internal void SetXsdFile(string xsdFile)
        {
            if (xsdFile == null)
            {
                _schemaParser = null;
            }
            else if (_schemaParser == null || _schemaParser.Filename != xsdFile)
            {
                _schemaParser = new SchemaParser(xsdFile, true);
            }
        }

    }
}
