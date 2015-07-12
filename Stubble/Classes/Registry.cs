﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Stubble.Core.Classes.Tokens;
using Stubble.Core.Helpers;

namespace Stubble.Core.Classes
{
    public sealed class Registry
    {
        private static readonly string[] DefaultTokenTypes = { @"\/", "=", @"\{", "!" };
        private static readonly string[] ReservedTokens = { "name", "text" }; //Name and text are used internally for tokens so must exist

        public IReadOnlyDictionary<Type, Func<object, string, object>> ValueGetters { get; private set; }
        //public IReadOnlyDictionary<string, Func<string, Tags, ParserOutput>> TokenGetters { get; private set; }
        public TokenGetter[] TokenGetters { get; private set; }
        public IReadOnlyList<Func<object, bool?>> TruthyChecks { get; private set; }
        public Regex TokenMatchRegex { get; private set; }

        #region Default Value Getters
        private static readonly IDictionary<Type, Func<object, string, object>> DefaultValueGetters = new Dictionary
            <Type, Func<object, string, object>>
        {
            {
                typeof (IDictionary<string, object>),
                (value, key) =>
                {
                    var castValue = value as IDictionary<string, object>;
                    return castValue != null && castValue.ContainsKey(key) ? castValue[key] : null;
                }
            },
            {
                typeof (IDictionary),
                (value, key) =>
                {
                    var castValue = value as IDictionary;
                    return castValue != null ? castValue[key] : null;
                }
            },
            {
                typeof (object), (value, key) =>
                {
                    var type = value.GetType();
                    var memberArr = type.GetMember(key, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (memberArr.Length != 1) return null;

                    var member = memberArr[0];
                    switch (member.MemberType)
                    {
                        case MemberTypes.Field:
                            return ((FieldInfo)member).GetValue(value);
                        case MemberTypes.Property:
                            return ((PropertyInfo)member).GetValue(value, null);
                        case MemberTypes.Method:
                            var methodMember = (MethodInfo) member;
                            return methodMember.GetParameters().Length == 0
                                ? methodMember.Invoke(value, null)
                                : null;
                        default:
                            return null;
                    }
                }
            }
        };
        #endregion
        #region Default Token Getters
        private static readonly IDictionary<string, Func<string, Tags, ParserOutput>> DefaultTokenGetters = new Dictionary
            <string, Func<string, Tags, ParserOutput>>
        {
            { "#", (s, tags) => new SectionToken() { TokenType = s, Tags = tags } },
            { "^", (s, tags) => new InvertedToken() { TokenType = s } },
            { ">", (s, tags) => new PartialToken() { TokenType = s } },
            { "&", (s, tags) => new UnescapedValueToken() { TokenType = s } },
            { "name", (s, tags) => new EscapedValueToken() { TokenType = s } },
            { "text", (s, tags) => new RawValueToken() { TokenType = s } }
        };
        #endregion

        public Registry()
        {
            ValueGetters = new ReadOnlyDictionary<Type, Func<object, string, object>>(DefaultValueGetters);
            TokenGetters = DefaultTokenGetters.Select(x => new TokenGetter {Getter = x.Value, TokenType = x.Key}).ToArray();
            TruthyChecks = new List<Func<object, bool?>>();
            TokenMatchRegex = new Regex(
                string.Join("|", TokenGetters.Where(s => !ReservedTokens.Contains(s.TokenType))
                                        .Select(s => Parser.EscapeRegexExpression(s.TokenType))
                                        .Concat(DefaultTokenTypes)));
        }

        public Registry(IDictionary<Type, Func<object, string, object>> valueGetters, IDictionary<string, Func<string, Tags, ParserOutput>> tokenGetters, IReadOnlyList<Func<object, bool?>> truthyChecks)
        {
            SetValueGetters(valueGetters);
            SetTokenGetters(tokenGetters);
            TruthyChecks = truthyChecks;
        }

        private void SetValueGetters(IDictionary<Type, Func<object, string, object>> valueGetters)
        {
            var mergedGetters = DefaultValueGetters.MergeLeft(valueGetters);

            mergedGetters = mergedGetters
                .OrderBy(x => x.Key, TypeBySubclassAndAssignableImpl.TypeBySubclassAndAssignable())
                .ToDictionary(item => item.Key, item => item.Value);

            ValueGetters = new ReadOnlyDictionary<Type, Func<object, string, object>>(mergedGetters);
        }

        private void SetTokenGetters(IDictionary<string, Func<string, Tags, ParserOutput>> tokenGetters)
        {
            var mergedGetters = DefaultTokenGetters.MergeLeft(tokenGetters);

            TokenGetters = mergedGetters.Select(x => new TokenGetter { Getter = x.Value, TokenType = x.Key }).ToArray();
        }
    }
}
