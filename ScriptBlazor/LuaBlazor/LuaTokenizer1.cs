﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScriptBlazor.LuaBlazor
{
    //Lua 5.2 tokenizer to correctly handle Lua code.
    internal sealed class LuaTokenizer1
    {
        public enum TokenType
        {
            //We are using UTF16, so starting from 65536 instead of 256.
            First = 65536,
            And, Break,
            Do, Else, Elseif, End, False, For, Function,
            Goto, If, In, Local, Nil, Not, Or, Repeat,
            Return, Then, True, Until, While,

            Concat, Dots, Eq, Ge, Le, Ne, Dbcolon, Eos,
            Number, Name, String,
        }

        private static readonly Dictionary<string, TokenType> _keywords = new()
        {
            { "do", TokenType.Do },
            { "else", TokenType.Else },
            { "elseif", TokenType.Elseif },
            { "end", TokenType.End },
            { "false", TokenType.False },
            { "for", TokenType.For },
            { "function", TokenType.Function },
            { "goto", TokenType.Goto },
            { "if", TokenType.If },
            { "in", TokenType.In },
            { "local", TokenType.Local },
            { "nil", TokenType.Nil },
            { "not", TokenType.Not },
            { "or", TokenType.Or },
            { "repeat", TokenType.Repeat },
            { "return", TokenType.Return },
            { "then", TokenType.Then },
            { "true", TokenType.True },
            { "until", TokenType.Until },
            { "while", TokenType.While },
        };

        private readonly PeekableTokenSequence _input;
        private readonly StringBuilder _identifierBuilder = new();
        private readonly StringBuilder _copyToOutput;

        private char _currentChar;
        private int _currentPos;

        private TokenType _current;

        public LuaTokenizer1(PeekableTokenSequence input, StringBuilder copyToOutput)
        {
            _input = input;
            _copyToOutput = copyToOutput;
            Reset();
        }

        //Write currently consumed content into copyToOutput.
        public void Flush()
        {
            if (_currentPos != 0)
            {
                _input.SplitCurrent(_currentPos);
                SkipCurrentInputToken();
            }
        }

        //After temporarily disconnected to input (which has been read from outside)
        //use this method to reconnect. It effectively reinitialize the current pos and char.
        public void Reset()
        {
            while (_input.Current.Content.Length == 0)
            {
                _input.EnsureMoveNext();
            }
            _currentPos = 0;
            _currentChar = _input.Current.Content[0];
        }

        private char NextChar()
        {
            if (_currentChar != default)
            {
                _copyToOutput.Append(_currentChar);
            }
            if (++_currentPos == _input.Current.Content.Length)
            {
                return _currentChar = SkipCurrentInputToken();
            }
            return _currentChar = _input.Current.Content[_currentPos];
        }

        private char PeekChar(int pos)
        {
            int peek = 0;
            while (_input.TryPeek(peek, out var templateToken))
            {
                if (templateToken.Content.Length > pos)
                {
                    return templateToken.Content[pos];
                }
                pos -= templateToken.Content.Length;
                peek += 1;
            }
            return default;
        }

        private char SkipCurrentInputToken()
        {
            if (_currentPos < _input.Current.Content.Length)
            {
                _copyToOutput.Append(_input.Current.Content[_currentPos..]);
            }
            _currentPos = 0;
            do
            {
                if (!_input.MoveNext())
                {
                    _currentPos = -1;
                    return _currentChar = default;
                }
            } while (_input.Current.Content.Length == 0);
            return _currentChar = _input.Current.Content[0];
        }

        //============================================

        public bool MoveNext()
        {
            if (_current == TokenType.Eos)
            {
                return false;
            }
            _current = Tokenize();
            return true;
        }

        public void EnsumeMoveNext()
        {
            if (!MoveNext())
            {
                //This is an internal exception.
                throw new Exception();
            }
        }

        public TokenType Current => _current;

        private static TokenType MakeToken(char c)
        {
            return (TokenType)c;
        }

        private TokenType Tokenize()
        {
            while (true)
            {
                switch (_currentChar)
                {
                case '\n':
                case '\r':
                case ' ':
                case '\f':
                case '\t':
                case '\v':
                {
                    NextChar();
                    break;
                }
                case '-': //'-' or '--'
                {
                    if (NextChar() != '-')
                    {
                        //Not a comment.
                        return MakeToken('-');
                    }
                    if (NextChar() == '[')
                    {
                        var sep = SkipSep();
                        if (sep >= 0)
                        {
                            //Long comment.
                            ReadLongString(sep);
                            break;
                        }
                    }
                    //Short comment.
                    while (_currentChar != '\r' && _currentChar != '\n' && _currentChar != default)
                    {
                        NextChar();
                    }
                    break;
                }
                case '[': //long string or '['
                {
                    var sep = SkipSep();
                    if (sep >= 0)
                    {
                        ReadLongString(sep);
                        return TokenType.String;
                    }
                    else if (sep == -1)
                    {
                        return MakeToken('[');
                    }
                    else
                    {
                        throw new Exception("Invalid long string delimiter");
                    }
                }
                case '=': //'=' or eq
                {
                    if (NextChar() != '=')
                    {
                        return MakeToken('=');
                    }
                    NextChar();
                    return TokenType.Eq;
                }
                case '<': //'<' or le
                {
                    if (NextChar() != '=')
                    {
                        return MakeToken('<');
                    }
                    NextChar();
                    return TokenType.Le;
                }
                case '>': //'>' or ge
                {
                    if (NextChar() != '=')
                    {
                        return MakeToken('>');
                    }
                    NextChar();
                    return TokenType.Ge;
                }
                case '~': //'~' or ne
                {
                    if (NextChar() != '=')
                    {
                        return MakeToken('~');
                    }
                    NextChar();
                    return TokenType.Ne;
                }
                case ':': //':' or dbcolon
                {
                    if (NextChar() != ':')
                    {
                        return MakeToken(':');
                    }
                    NextChar();
                    return TokenType.Dbcolon;
                }
                case '"': //short literal string
                case '\'':
                {
                    ReadString();
                    return TokenType.String;
                }
                case '.': //'.', concat, dots or number
                {
                    var next = PeekChar(1);
                    if (next == '.')
                    {
                        //Not a number.
                        NextChar(); //Skip the first '.'.
                        NextChar(); //Skip the second '.'.
                        if (NextChar() != '.')
                        {
                            return TokenType.Concat;
                        }
                        NextChar(); //SKip the third '.'.
                        return TokenType.Dots;
                    }
                    else if (next >= '0' && next <= '9')
                    {
                        ReadNumeral();
                        return TokenType.Number;
                    }
                    else
                    {
                        NextChar();
                        return MakeToken('.');
                    }
                }
                case >= '0' and <= '9': //number
                {
                    ReadNumeral();
                    return TokenType.Number;
                }
                case default(char):
                    return TokenType.Eos;
                default:
                    if (_input.Current.Type == TemplateTokenizer.TokenType.Text)
                    {
                        //Identifier or keyword.
                        var ret = ReadIdentifier();
                        if (_keywords.TryGetValue(ret, out var keyword))
                        {
                            return keyword;
                        }
                        return TokenType.Name;
                    }
                    else
                    {
                        //Single char token.
                        var retChar = _currentChar;
                        NextChar();
                        return MakeToken(retChar);
                    }
                }
            }
        }

        //Behavior:
        //If it's a valid sep, skip the sep.
        //If it's a "[" (or "]") (or an invalid sep), only skip the '[' (or ']').
        //Note that for invalid sep the caller should throw (except used as comments) so it does not matter.
        private int SkipSep()
        {
            var s = _currentChar;
            NextChar(); //Skip '[' or ']'.

            int count = 0;
            for (; PeekChar(count) == '='; ++count)
            {
            }

            if (PeekChar(count) == s)
            {
                for (int i = 0; i < count + 1; ++i)
                {
                    NextChar();
                }
                return count;
            }
            return -count - 1;
        }

        //Starting sep has been skipped (this is different from original Lua impl).
        private void ReadLongString(int sep)
        {
            while (true)
            {
                switch (_currentChar)
                {
                case default(char):
                    throw new Exception("Unfinished long string or comment");
                case ']':
                    if (SkipSep() == sep)
                    {
                        //Success.
                        return;
                    }
                    break;
                default:
                    NextChar();
                    break;
                }
            }
        }

        private void ReadString()
        {
            var s = _currentChar;
            NextChar();
            while (_currentChar != s)
            {
                if (_currentChar == '\\')
                {
                    if (PeekChar(1) == s)
                    {
                        NextChar();
                        NextChar();
                        continue;
                    }
                }
            }
            NextChar(); //Skip the end '"' or '\''.
        }

        private void ReadNumeral()
        {
            var nextChar = PeekChar(1);
            var useBinaryExp = _currentChar == '0' && nextChar == 'x' || nextChar == 'X';
            if (useBinaryExp)
            {
                NextChar();
                NextChar();
            }
            bool IsNumeralChar(char c)
            {
                //Lua allows decimal point in current culture. We don't allow it here for simplicity.
                return c >= '0' && c <= '9' ||
                    c == '+' || c == '-' || c == '.' ||
                    char.ToLower(c) == (useBinaryExp ? 'p' : 'e');
            }
            while (IsNumeralChar(NextChar()))
            {
            }
        }

        private string ReadIdentifier()
        {
            _identifierBuilder.Clear();
            Flush(); //Start as a new template token (so we can directly Append its content).

            while (_input.Current.Type == TemplateTokenizer.TokenType.Text)
            {
                _identifierBuilder.Append(_input.Current.Content);
                SkipCurrentInputToken();
            }
            return _identifierBuilder.ToString();
        }
    }
}
