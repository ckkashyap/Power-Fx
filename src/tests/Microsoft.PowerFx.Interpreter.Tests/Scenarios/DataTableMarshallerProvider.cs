﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Data;
using Microsoft.PowerFx.Interpreter.Tests;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Tests
{
    /// <summary>
    /// Wire up <see cref="DataTable"/> conversions to <see cref="TypeMarshallerCache.Marshal{T}(T)"/>.     
    /// </summary>
    internal class DataTableMarshallerProvider : IDynamicTypeMarshaller
    {
        public bool TryMarshal(TypeMarshallerCache cache, object value, out FormulaValue result)
        {
            if (value is DataTable dataTable)
            {
                result = new DataTableValue(dataTable);
                return true;
            }

            result = null;
            return false;
        }
    }

    // Wrap a System.Data.DataTable as a Power Fx TableValue
    // Marshal DataTable as a Table of Records. 
    // Use the column's type info, if available; else marshal as untyped object.
    // All marshalling is done lazily so we avoid copying the whole table.
    internal class DataTableValue : CollectionTableValue<DataRow>
    {
        public DataTableValue(DataTable dataTable)
            : base(ComputeType(dataTable), new DataTableWrapper(dataTable))
        {
        }

        public static RecordType ComputeType(DataTable dataTable)
        {
            var recordType = new RecordType();
            foreach (DataColumn column in dataTable.Columns)
            {
                if (!PrimitiveValueConversions.TryGetFormulaType(column.DataType, out var fxType))
                {
                    fxType = FormulaType.UntypedObject;
                }

                recordType = recordType.Add(column.ColumnName, fxType);
            }

            return recordType;
        }

        protected override DValue<RecordValue> Marshal(DataRow item)
        {
            // We could check item.RowError and return an error for the whole row. 
            
            // Return value
            var record = new DataRowRecordValue(RecordType, item);
            return DValue<RecordValue>.Of(record);
        }

        // Wrap an individual DataRow of the DataTable as a Power Fx RecordValue
        private class DataRowRecordValue : RecordValue
        {
            private readonly DataRow _row;

            public DataRowRecordValue(RecordType type, DataRow row)
                : base(type)
            {
                _row = row;
            }

            // DataRow doesn't have a TryGetValue! 
            // So just catch the exception.
            private bool TryGetValue(DataRow row, string fieldName, out object value)
            {
                try
                {
                    value = _row[fieldName];
                    return true;
                }
                catch
                {
                    value = null;
                    return false;
                }
            }

            protected override bool TryGetField(FormulaType fieldType, string fieldName, out FormulaValue result)
            {
                // DataRow doesn't have a way to check if 
                if (!TryGetValue(_row, fieldName, out var value))
                {
                    result = null;
                    return false;
                }

                // The expected fieldType was determined in ComputeType based on Column.DataType.
                if (fieldType == FormulaType.UntypedObject)
                {
                    result = PrimitiveWrapperAsUnknownObject.New(value);
                } 
                else
                {
                    result = PrimitiveValueConversions.Marshal(value, fieldType);
                }

                return true;
            }  
        }
    }

    // This class shouldn't be necessary, but DataTable is legacy and doesn't implement generic interfaces. 
    // Wrap and expose the generic interfaces that it ought to be implementing. 
    internal class DataTableWrapper : IReadOnlyList<DataRow>
    {
        private readonly DataTable _dataTable;

        public int Count => _dataTable.Rows.Count;

        public DataRow this[int index] => _dataTable.Rows[index];

        public DataTableWrapper(DataTable dataTable)
        {
            _dataTable = dataTable;
        }

        public IEnumerator<DataRow> GetEnumerator()
        {
            // DataTable is legacy and doesn't implement generic interfaces. 
            foreach (DataRow row in _dataTable.Rows)
            {
                yield return row;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _dataTable.Rows.GetEnumerator();
        }
    }    
}
