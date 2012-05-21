﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Text;

namespace NPoco
{
    internal class MultiPocoFactory
    {
        public List<Delegate> m_Delegates;
        public Delegate GetItem(int index) { return m_Delegates[index]; }

        // Automagically guess the property relationships between various POCOs and create a delegate that will set them up
        public static object GetAutoMapper(Type[] types)
        {
            // Build a key
            var key = string.Join(":", types.AsEnumerable());

            return AutoMappers.Get(key, () =>
            {
                // Create a method
                var m = new DynamicMethod("poco_automapper", types[0], types, true);
                var il = m.GetILGenerator();

                for (int i = 1; i < types.Length; i++)
                {
                    bool handled = false;
                    for (int j = i - 1; j >= 0; j--)
                    {
                        // Find the property
                        var candidates = types[j].GetProperties().Where(p => p.PropertyType == types[i]).ToList();
                        if (candidates.Count == 0)
                            continue;
                        if (candidates.Count > 1)
                            throw new InvalidOperationException(string.Format("Can't auto join {0} as {1} has more than one property of type {0}", types[i], types[j]));

                        // Generate code
                        il.Emit(OpCodes.Ldarg_S, j);
                        il.Emit(OpCodes.Ldarg_S, i);
                        il.Emit(OpCodes.Callvirt, candidates[0].GetSetMethod(true));
                        handled = true;
                    }

                    if (!handled)
                        throw new InvalidOperationException(string.Format("Can't auto join {0}", types[i]));
                }

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ret);

                // Cache it
                var del = m.CreateDelegate(Expression.GetFuncType(types.Concat(types.Take(1)).ToArray()));
                return del;
            });
        }

         // Find the split point in a result set for two different pocos and return the poco factory for the first
        static Delegate FindSplitPoint(Type typeThis, Type typeNext, string sql, string connectionString, IDataReader r, ref int pos)
        {
            // Last?
            if (typeNext == null)
                return PocoData.ForType(typeThis).GetFactory(sql, connectionString, pos, r.FieldCount - pos, r, null);

            // Get PocoData for the two types
            PocoData pdThis = PocoData.ForType(typeThis);
            PocoData pdNext = PocoData.ForType(typeNext);

            // Find split point
            int firstColumn = pos;
            var usedColumns = new Dictionary<string, bool>();
            for (; pos < r.FieldCount; pos++)
            {
                // Split if field name has already been used, or if the field doesn't exist in current poco but does in the next
                string fieldName = r.GetName(pos);
                if (usedColumns.ContainsKey(fieldName) || (!pdThis.Columns.ContainsKey(fieldName) && pdNext.Columns.ContainsKey(fieldName)))
                {
                    return pdThis.GetFactory(sql, connectionString, firstColumn, pos - firstColumn, r, null);
                }
                usedColumns.Add(fieldName, true);
            }

            throw new InvalidOperationException(string.Format("Couldn't find split point between {0} and {1}", typeThis, typeNext));
        }

        // Create a multi-poco factory
        static Func<IDataReader, object, TRet> CreateMultiPocoFactory<TRet>(Type[] types, string sql, string connectionString, IDataReader r)
        {
            var m = new DynamicMethod("poco_multipoco_factory", typeof(TRet), new Type[] { typeof(MultiPocoFactory), typeof(IDataReader), typeof(object) }, typeof(MultiPocoFactory));
            var il = m.GetILGenerator();

            // Load the callback
            il.Emit(OpCodes.Ldarg_2);

            // Call each delegate
            var dels = new List<Delegate>();
            int pos = 0;
            for (int i=0; i<types.Length; i++)
            {
                // Add to list of delegates to call
                var del = FindSplitPoint(types[i], i + 1 < types.Length ? types[i + 1] : null, sql, connectionString, r, ref pos);
                dels.Add(del);

                // Get the delegate
                il.Emit(OpCodes.Ldarg_0);													// callback,this
                il.Emit(OpCodes.Ldc_I4, i);													// callback,this,Index
                il.Emit(OpCodes.Callvirt, typeof(MultiPocoFactory).GetMethod("GetItem"));	// callback,Delegate
                il.Emit(OpCodes.Ldarg_1);													// callback,delegate, datareader
                il.Emit(OpCodes.Ldnull);                                                    // callback,delegate, datareader,null

                // Call Invoke
                var tDelInvoke = del.GetType().GetMethod("Invoke");
                il.Emit(OpCodes.Callvirt, tDelInvoke);										// Poco left on stack
            }

            // By now we should have the callback and the N pocos all on the stack.  Call the callback and we're done
            il.Emit(OpCodes.Callvirt, Expression.GetFuncType(types.Concat(new Type[] { typeof(TRet) }).ToArray()).GetMethod("Invoke"));
            il.Emit(OpCodes.Ret);

            // Finish up
            return (Func<IDataReader, object, TRet>)m.CreateDelegate(typeof(Func<IDataReader, object, TRet>), new MultiPocoFactory() { m_Delegates = dels });
        }

        // Various cached stuff
        static Cache<string, object> MultiPocoFactories = new Cache<string, object>();
        static Cache<string, object> AutoMappers = new Cache<string, object>();

        // Get (or create) the multi-poco factory for a query
        public static Func<IDataReader, object, TRet> GetMultiPocoFactory<TRet>(Type[] types, string sql, string connectionString, IDataReader r)
        {
            // Build a key string  (this is crap, should address this at some point)
            var kb = new StringBuilder();
            kb.Append(typeof(TRet).ToString());
            kb.Append(":");
            foreach (var t in types)
            {
                kb.Append(":" + t);
            }
            kb.Append(":" + connectionString);
            kb.Append(":" + sql);
            string key = kb.ToString();

            return (Func<IDataReader, object, TRet>)MultiPocoFactories.Get(key, () => CreateMultiPocoFactory<TRet>(types, sql, connectionString, r));
        }
    }

       
}
