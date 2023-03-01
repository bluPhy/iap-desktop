﻿//
// Copyright 2023 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Google.Solutions.Mvvm.Format
{
    /// <summary>
    /// A Markdown document.
    /// </summary>
    internal class MarkdownDocument
    {
        public DocumentNode Root { get; }

        private static readonly char[] NonLineBreakingWhitespace = new char[] { ' ', '\t' };
        private static readonly char[] UnorderedListBullets = new char[] { '*', '-', '+' };

        public static MarkdownDocument Parse(TextReader reader)
        {
            //
            // There's no proper grammar for Markdown, so we can't use a classic
            // lexer/parser architecture to read Markdown.
            //
            // CommonMark suggests a 2-phase parsing approach [1], which is what
            // we're using here.
            //
            // - In the first stage, we dissect the document into blocks (headings,
            //   paragraphs, list items, etc)
            // - In the second stage, we parse text blocks to resolve emphases,
            //   links, etc.
            //
            // Note that we're only supporting a subset of Markdown syntax
            // features here.
            //
            // [1] https://spec.commonmark.org/0.30/#appendix-a-parsing-strategy
            //
            return new MarkdownDocument(DocumentNode.Parse(reader));
        }

        public static MarkdownDocument Parse(string markdown)
        {
            using (var reader = new StringReader(markdown))
            {
                return Parse(reader);
            }
        }

        private MarkdownDocument(DocumentNode root)
        {
            this.Root = root;
        }

        public override string ToString()
        {
            return this.Root.ToString();
        }

        //---------------------------------------------------------------------
        // Inner classes for blocks.
        //---------------------------------------------------------------------

        /// <summary>
        /// A node in a Markdown document tree.
        /// </summary>
        public abstract class Node
        {
            private Node next;
            private Node firstChild;
            protected Node lastChild; // TODO: make property

            public virtual IEnumerable<Node> Children
            {
                get
                {
                    if (this.firstChild == null)
                    {
                        yield break;
                    }
                    else
                    {
                        for (var block = this.firstChild; block != null;  block = block.next)
                        {
                            yield return block;
                        }
                    }
                }
            }

            protected void AppendNode(Node block)
            {
                if (this.firstChild == null)
                {
                    Debug.Assert(this.lastChild == null);
                    this.firstChild = block;
                    this.lastChild = this.firstChild;
                }
                else
                {
                    this.lastChild.next = block;
                    this.lastChild = block;
                }
            }

            protected virtual Node CreateNode(string line)
            {
                if (UnorderedListItemNode.IsUnorderedListItemNode(line))
                {
                    return new UnorderedListItemNode(line);
                }
                else if (OrderedListItemNode.IsOrderedListItemNode(line))
                {
                    return new OrderedListItemNode(line);
                }
                else
                {
                    return new SpanNode(line);
                }
            }

            protected virtual bool TryConsume(string line)
            {
                if (this.lastChild != null && this.lastChild.TryConsume(line))
                {
                    //
                    // Continuation of last block.
                    //
                    return true;
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    //
                    // An empty line always ends a block, but does
                    // not start a new one yet.
                    //
                    AppendNode(new ParagraphBreak());
                    return false;
                }
                else
                {
                    //
                    // Last block is closed, append a new block.
                    //
                    AppendNode(CreateNode(line));
                    return true;
                }
            }

            public abstract string Value { get; }

            public override string ToString()
            {
                var buffer = new StringBuilder();
                
                void Visit(Node block, int level)
                {
                    buffer.Append(new string(' ', level));
                    buffer.Append(block.Value);
                    buffer.Append('\n');

                    foreach (var child in block.Children)
                    {
                        Visit(child, level + 1);
                    }
                }

                Visit(this, 0);

                return buffer.ToString(); ;
            }
        }

        /// <summary>
        /// A break between two pararaphs, typically created by an
        /// empty line.
        /// </summary>
        public class ParagraphBreak : Node
        {
            public override string Value => "[ParagraphBreak]";

            protected override bool TryConsume(string line)
            {
                return false;
            }
        }

        /// <summary>
        /// A heading.
        /// </summary>
        public class HeadingNode : Node
        {
            public int Level { get; }
            public string Text { get; }

            public static bool IsHeadingNode(string line)
            {
                var index = line.IndexOfAny(NonLineBreakingWhitespace);
                return index > 0 && line.Substring(0, index).All(c => c == '#');
            }

            public HeadingNode(string line)
            {
                Debug.Assert(IsHeadingNode(line));

                var whitespaceIndex = line.IndexOfAny(NonLineBreakingWhitespace);
                this.Level = line.Substring(0, whitespaceIndex).Count();
                this.Text = line.Substring(whitespaceIndex).Trim();
            }

            protected override bool TryConsume(string line)
            {
                //
                // Headings are always single-line.
                //
                return false;
            }

            public override string Value => $"[Heading level={this.Level}] {this.Text}";
        }

        ///// <summary>
        ///// Inline text block. The text might contain links and emphasis,
        ///// but we don't parse these at this stage.
        ///// </summary>
        //public class TextNode : Node
        //{
        //    public string Text { get; private set; }

        //    public TextNode(string text)
        //    {
        //        this.Text = text;
        //    }

        //    // TODO: Override Children, parse text

        //    protected override bool TryConsume(string line)
        //    {
        //        if (string.IsNullOrWhiteSpace(line))
        //        {
        //            return false;
        //        }
        //        else
        //        {
        //            this.Text += " " + line;
        //            return true;
        //        }
        //    }

        //    public override string Value => "[Text] " + this.Text;
        //}

        /// <summary>
        /// Ordered list item.
        /// </summary>
        public class OrderedListItemNode : Node
        {
            public string Indent { get; }

            public static bool IsOrderedListItemNode(string line)
            {
                var dotIndex = line.IndexOf('.');

                return dotIndex > 0 &&
                    dotIndex < line.Length - 1 &&
                    line[dotIndex + 1] == ' ' &&
                    line.Substring(0, dotIndex).All(char.IsDigit);
            }

            public OrderedListItemNode(string line)
            {
                Debug.Assert(IsOrderedListItemNode(line));

                var indent = line.IndexOf(' ');
                while (line[indent] == ' ')
                {
                    indent++;
                }

                this.Indent = new string(' ', indent);

                AppendNode(new SpanNode(line.Substring(indent)));
            }

            protected override bool TryConsume(string line)
            {
                if (string.IsNullOrEmpty(line))
                {
                    AppendNode(new ParagraphBreak());
                    return true;
                }
                else if (!line.StartsWith(this.Indent))
                {
                    //
                    // Line doesn't have the minimum amount of indentation,
                    // so it can't be a continuation.
                    //
                    // NB. We don't support lazy continations.
                    //
                    return false;
                }
                else
                {
                    return base.TryConsume(line.Substring(this.Indent.Length));
                }
            }

            public override string Value 
                => $"[OrderedListItem indent={this.Indent.Length}]";
        }

        /// <summary>
        /// Unodered list item.
        /// </summary>
        public class UnorderedListItemNode : Node
        {
            public char Bullet { get;}
            public string Indent { get; }

            public static bool IsUnorderedListItemNode(string line)
            {
                return line.Length >= 3 && 
                    UnorderedListBullets.Contains(line[0]) && 
                    NonLineBreakingWhitespace.Contains(line[1]);
            }


            public UnorderedListItemNode(string line)
            {
                Debug.Assert(IsUnorderedListItemNode(line));

                this.Bullet = line[0];

                var indent = 1;
                while (line[indent] == ' ')
                {
                    indent++;
                }

                this.Indent = new string(' ', indent);

                AppendNode(new SpanNode(line.Substring(indent)));
            }

            protected override bool TryConsume(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    AppendNode(new ParagraphBreak());
                    return true;
                }
                else if (!line.StartsWith(this.Indent))
                {
                    //
                    // Line doesn't have the minimum amount of indentation,
                    // so it can't be a continuation.
                    //
                    // NB. We don't support lazy continations.
                    //
                    return false;
                }
                else
                {
                    return base.TryConsume(line.Substring(this.Indent.Length));
                }
            }

            public override string Value
                => $"[UnorderedListItem bullet={this.Bullet} indent={this.Indent.Length}]";
        }

        /// <summary>
        /// Document, this forms the root of the tree.
        /// </summary>
        public class DocumentNode : Node
        {
            protected override Node CreateNode(string line)
            {
                if (HeadingNode.IsHeadingNode(line))
                {
                    return new HeadingNode(line);
                }
                else
                {
                    return base.CreateNode(line);
                }
            }

            public override string Value => "[Document]";

            public static DocumentNode Parse(TextReader reader)
            {
                var document = new DocumentNode();
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                    else
                    {
                        document.TryConsume(line);
                    }
                }

                return document;
            }
        }

        //---------------------------------------------------------------------
        // Inner classes for spans.
        //---------------------------------------------------------------------

        internal enum TokenType
        {
            Text,
            Delimiter
        }

        /// <summary>
        /// A token withing a text span.
        /// </summary>
        internal class Token
        {
            public TokenType Type { get; }
            public string Value { get; }

            internal Token(TokenType type, string value)
            {
                Debug.Assert(type != TokenType.Delimiter || value.Length <= 2);

                this.Type = type;
                this.Value = value;
            }

            public static IEnumerable<Token> Tokenize(string text)
            {
                var textStart = -1;
                for (int i = 0; i < text.Length; i++)
                {
                    switch (text[i])
                    {
                        case '*':
                            {
                                if (textStart >= 0 && i - textStart > 0)
                                {
                                    //
                                    // Flush previous text token, if non-empty.
                                    //
                                    yield return new Token(TokenType.Text, text.Substring(textStart, i - textStart));
                                    textStart = -1;
                                }

                                if (i + 1 < text.Length && text[i + 1] == '*')
                                {
                                    i++;
                                    yield return new Token(TokenType.Delimiter, "**");
                                }
                                else
                                {
                                    yield return new Token(TokenType.Delimiter, "*");
                                }
                                break;
                            }
                        case '_':
                        case '[':
                        case ']':
                        case '(':
                        case ')':
                            //
                            // Delimeter.
                            //
                            if (textStart >= 0 && i - textStart > 0 )
                            {
                                //
                                // Flush previous text token, if non-empty.
                                //
                                yield return new Token(TokenType.Text, text.Substring(textStart, i - textStart));
                                textStart = -1;
                            }

                            yield return new Token(TokenType.Delimiter, text[i].ToString());
                            break;

                        default:
                            //
                            // Text.
                            //
                            if (textStart == -1)
                            {
                                textStart = i;
                            }
                            break;
                    }
                }

                if (textStart >= 0)
                {
                    yield return new Token(TokenType.Text, text.Substring(textStart));
                }
            }

            public override string ToString()
            {
                return $"{this.Type}: {this.Value}";
            }

            public override bool Equals(object obj)
            {
                return obj is Token token &&
                    token.Type == this.Type &&
                    token.Value == this.Value;
            }

            public static bool operator==(Token lhs, Token rhs)
            {
                if (lhs is null)
                {
                    return rhs is null;
                }
                else
                {
                    return lhs.Equals(rhs);
                }
            }

            public static bool operator !=(Token lhs, Token rhs) => !(lhs == rhs);

            public override int GetHashCode()
            {
                return this.Value.GetHashCode();
            }
        }

        public class SpanNode : Node
        {
            public override string Value => $"[Span]";

            internal SpanNode(string text)
            {
                TryConsume(text);
            }

            protected SpanNode()
            {
            }

            protected SpanNode CreateSpanNode(Token token, IEnumerable<Token> remainder)
            {
                if (token.Type == TokenType.Text)
                {
                    return new TextNode(token.Value, true);
                }
                
                if ((token.Value == "_" || token.Value == "*" || token.Value == "**") &&
                    remainder.FirstOrDefault() is Token next &&
                    next != null &&
                    next.Type == TokenType.Text &&
                    next.Value.Length > 1 &&
                    !NonLineBreakingWhitespace.Contains(next.Value[0]))
                {
                    return new EmphasisNode(token.Value);
                }
                else if (token.Value == "[" &&
                    remainder
                        .SkipWhile(t => t != new Token(TokenType.Delimiter, "]"))
                        .Skip(1)
                        .FirstOrDefault() == new Token(TokenType.Delimiter, "("))
                {
                    return new LinkSpanNode();
                }
                else
                {
                    return new TextNode(token.Value, false);
                }
            }

            protected override sealed bool TryConsume(string line)
            {
                if (this.lastChild != null && string.IsNullOrWhiteSpace(line))
                {
                    // Spans must not extend beyond one paragraph.
                    return false;
                }

                var tokens = Token.Tokenize(line);
                while (tokens.Any())
                {
                    var token = tokens.First();
                    var remainder = tokens.Skip(1);
                    TryConsume(token, remainder);
                    tokens = remainder;
                }

                return true;
            }

            protected virtual bool TryConsume(Token token, IEnumerable<Token> remainder)
            {
                if (this.lastChild != null && 
                    ((SpanNode)this.lastChild).TryConsume(token, remainder))
                {
                    //
                    // Continuation of last span.
                    //
                    return true;
                }
                else
                {
                    //
                    // Last block is closed, append a new block.
                    //
                    AppendNode(CreateSpanNode(token, remainder));
                    return true;
                }
            }

            public static SpanNode Parse(string markdown)
            {
                var node = new SpanNode();
                node.TryConsume(markdown);
                return node;
            }
        }

        public class TextNode : SpanNode
        {
            private readonly bool space;
            public string Text { get; protected set; }

            public override string Value => $"[Text] {this.Text}";

            public TextNode(string text, bool space)
            {
                this.Text = text;
                this.space = space;
            }

            protected override bool TryConsume(Token token, IEnumerable<Token> remainder)
            {
                if (token.Type == TokenType.Delimiter)
                {
                    return false;
                }
                else
                {
                    if (this.space)
                    {
                        this.Text = this.Text + " " + token.Value;
                    }
                    else
                    {
                        this.Text += token.Value;
                    }
                    return true;
                }
            }
        }

        public class EmphasisNode : SpanNode
        {
            private bool bodyCompleted = false;

            public string Text { get; protected set; }
            public string Delimiter { get; }

            public EmphasisNode(string delimiter)
            {
                this.Delimiter = delimiter;
            }

            public override string Value => $"[Emphasis delimiter={this.Delimiter}] {this.Text}";

            protected override bool TryConsume(Token token, IEnumerable<Token> remainder)
            {
                if (this.bodyCompleted)
                {
                    return false;
                }
                else if (token.Type == TokenType.Delimiter && token.Value == this.Delimiter)
                {
                    this.bodyCompleted = true;
                    return true;
                }
                else if (token.Type == TokenType.Text)
                {
                    this.Text += token.Value;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public class LinkSpanNode : SpanNode
        {
            private bool linkBodyCompleted = false;
            private bool linkHrefCompleted = false;
            public override string Value => $"[Link href={this.Href}]";
            public string Href { get; protected set; }

            protected override bool TryConsume(Token token, IEnumerable<Token> remainder)
            {
                if (this.linkHrefCompleted)
                {
                    //
                    // Link completed.
                    //
                    return false;
                }
                else if (this.linkBodyCompleted)
                {
                    //
                    // Building the link href.
                    //
                    if (this.Href == string.Empty && token == new Token(TokenType.Delimiter, "("))
                    {
                        return true;
                    }
                    else if (token == new Token(TokenType.Delimiter, ")"))
                    {
                        this.linkHrefCompleted = true;
                        return true;
                    }
                    else
                    {
                        this.Href += token.Value;
                        return true;
                    }
                }
                else
                {
                    //
                    // Building the link body/text.
                    //
                    if (token == new Token(TokenType.Delimiter, "]"))
                    {
                        this.linkBodyCompleted = true;
                        return true;
                    }
                    else
                    {
                        return base.TryConsume(token, remainder);
                    }
                }
            }
        }
    }
}
