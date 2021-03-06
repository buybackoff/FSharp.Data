﻿// --------------------------------------------------------------------------------------
// Tests for a utility that generates nice PascalCase and camelCase names for members
// --------------------------------------------------------------------------------------
module FSharp.Data.Tests.DesignTime.InferenceTests

#if INTERACTIVE
#r "../../packages/NUnit.2.6.3/lib/nunit.framework.dll"
#r "../../bin/FSharp.Data.DesignTime.dll"
#load "../Common/FsUnit.fs"
#endif

open FsUnit
open System
open System.IO
open FSharp.Data.Json
open NUnit.Framework
open FSharp.Data.Csv
open FSharp.Data.Runtime
open FSharp.Data.Runtime.StructuralTypes
open FSharp.Data.Runtime.StructuralInference
open ProviderImplementation
open ProviderImplementation.ProvidedTypes

/// A collection containing just one type
let SimpleCollection typ = 
  InferedType.Collection(Map.ofSeq [typeTag typ, (InferedMultiplicity.Multiple, typ)])

/// Returns a heterogeneous type that allows the specified type or null type
let WithNull typ = 
  InferedType.Heterogeneous(Map.ofSeq [typeTag typ, typ; typeTag InferedType.Null, InferedType.Null])

let culture = TextRuntime.GetCulture ""

let inferType = CsvInference.inferType ProvidedMeasureBuilder.Default.SI

[<Test>]
let ``Seq.pairBy helper function works``() = 
  let actual = Seq.pairBy fst [(2, "a"); (1, "b")] [(1, "A"); (3, "C")]
  let expected = 
    [ (1, Some (1, "b"), Some (1, "A"))
      (2, Some (2, "a"), None)
      (3, None, Some (3, "C")) ]
  set actual |> shouldEqual (set expected)

[<Test>]
let ``Seq.pairBy helper function preserves order``() = 
  let actual = Seq.pairBy fst [("one", "a"); ("two", "b")] [("one", "A"); ("two", "B")]
  let expected = 
    [ ("one", Some ("one", "a"), Some ("one", "A"))
      ("two", Some ("two", "b"), Some ("two", "B")) ] 
  actual |> shouldEqual expected

[<Test>]
let ``Finds common subtype of numeric types (decimal)``() =
  let source = JsonValue.Parse """[ 10, 10.23 ]"""
  let expected = SimpleCollection(InferedType.Primitive(typeof<decimal>, None))
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Finds common subtype of numeric types (int64)``() =
  let source = JsonValue.Parse """[ 10, 2147483648 ]"""
  let expected = SimpleCollection(InferedType.Primitive(typeof<int64>, None))
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Infers heterogeneous type of InferedType.Primitives``() =
  let source = JsonValue.Parse """[ 1,true ]"""
  let expected = 
    [ InferedTypeTag.Number, (Single, InferedType.Primitive(typeof<Bit1>, None))
      InferedTypeTag.Boolean, (Single, InferedType.Primitive(typeof<bool>, None)) ]
    |> Map.ofSeq |> InferedType.Collection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Infers heterogeneous type of InferedType.Primitives and nulls``() =
  let source = JsonValue.Parse """[ 1,true,null ]"""
  let expected = 
    [ InferedTypeTag.Null, (Single, InferedType.Null)
      InferedTypeTag.Number, (Single, InferedType.Primitive(typeof<Bit1>, None))
      InferedTypeTag.Boolean, (Single, InferedType.Primitive(typeof<bool>, None)) ]
    |> Map.ofSeq |> InferedType.Collection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Finds common subtype of numeric types (float)``() =
  let source = JsonValue.Parse """[ 10, 10.23, 79228162514264337593543950336 ]"""
  let expected = SimpleCollection(InferedType.Primitive(typeof<float>, None))
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Infers heterogeneous type of InferedType.Primitives and records``() =
  let source = JsonValue.Parse """[ {"a":0}, 1,2 ]"""
  let expected = 
    [ InferedTypeTag.Number, (Multiple, InferedType.Primitive(typeof<int>, None))
      InferedTypeTag.Record None, 
        (Single, InferedType.Record(None, [ { Name="a"; Optional=false; Type=InferedType.Primitive(typeof<Bit0>, None) } ])) ]
    |> Map.ofSeq |> InferedType.Collection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Merges types in a collection of collections``() =
  let source = JsonValue.Parse """[ [{"a":true},{"b":1}], [{"b":1.1}] ]"""
  let expected = 
    InferedType.Record(None, [ {Name = "a"; Optional = true; Type = InferedType.Primitive(typeof<bool>, None) }
                               {Name = "b"; Optional = true; Type = InferedType.Primitive(typeof<decimal>, None) } ])
    |> SimpleCollection |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected
    
[<Test>]
let ``Unions properties of records in a collection``() =
  let source = JsonValue.Parse """[ {"a":1, "b":""}, {"a":1.2, "c":true} ]"""
  let expected =
    InferedType.Record(None, [ {Name = "a"; Optional = false; Type = InferedType.Primitive(typeof<decimal>, None) }
                               {Name = "b"; Optional = true; Type = InferedType.Primitive(typeof<string>, None) }
                               {Name = "c"; Optional = true; Type = InferedType.Primitive(typeof<bool>, None) }])
    |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``InferedType.Null is a valid value of string``() =
  let source = JsonValue.Parse """[ {"a":null}, {"a":"b"} ]"""
  let expected =
    InferedType.Record(None, [ {Name = "a"; Optional = false; Type = InferedType.Primitive(typeof<string>, None) } ])
    |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``InferedType.Null is not a valid value of DateTime``() =
  let actual = 
    subtypeInfered false InferedType.Null (InferedType.Primitive(typeof<DateTime>, None))
  let expected = 
    [ InferedTypeTag.Null, InferedType.Null
      InferedTypeTag.DateTime, InferedType.Primitive(typeof<DateTime>, None) ]
    |> Map.ofSeq |> InferedType.Heterogeneous
  actual |> shouldEqual expected

[<Test>]
let ``Infers mixed fields of a a record as heterogeneous type with nulls (1.)``() =
  let source = JsonValue.Parse """[ {"a":null}, {"a":123} ]"""
  let expected =
    InferedType.Record(None, [ {Name = "a"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<int>, None)) } ])
    |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``InferedType.Null is a valid value of record``() =
  let source = JsonValue.Parse """[ {"a":null}, {"a":{"b": 1}} ]"""
  let nestedRecord = 
    InferedType.Record(None, [{ Name = "b"; Optional = false; Type = InferedType.Primitive(typeof<Bit1>, None) }])
  let expected =
    InferedType.Record(None, [ {Name = "a"; Optional = false; Type = nestedRecord } ])
    |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Infers mixed fields of a record as heterogeneous type``() =
  let source = JsonValue.Parse """[ {"a":"hi"}, {"a":2} , {"a":2147483648} ]"""
  let cases = 
    Map.ofSeq [ InferedTypeTag.String, InferedType.Primitive(typeof<string>, None) 
                InferedTypeTag.Number, InferedType.Primitive(typeof<int64>, None) ]
  let expected = 
    InferedType.Record(None, [ { Name = "a"; Optional = false; Type = InferedType.Heterogeneous cases }])
    |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Infers mixed fields of a record as heterogeneous type with nulls (2.)``() =
  let source = JsonValue.Parse """[ {"a":null}, {"a":2} , {"a":3} ]"""
  let cases = 
    Map.ofSeq [ InferedTypeTag.Null, InferedType.Null
                InferedTypeTag.Number, InferedType.Primitive(typeof<int>, None) ]
  let expected = 
    InferedType.Record(None, [ { Name = "a"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<int>, None)) }])
    |> SimpleCollection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Inference of multiple nulls works``() = 
  let source = JsonValue.Parse """[0, [{"a": null}, {"a":null}]]"""
  let prop = { Name = "a"; Optional = false; Type = InferedType.Null }
  let expected = 
    [ InferedTypeTag.Collection, (Single, SimpleCollection(InferedType.Record(None, [prop])))
      InferedTypeTag.Number, (Single, InferedType.Primitive(typeof<Bit0>, None)) ]
    |> Map.ofSeq |> InferedType.Collection
  let actual = JsonInference.inferType culture true source
  actual |> shouldEqual expected

[<Test>]
let ``Inference of DateTime``() = 
  let source = CsvFile.Parse("date,int,float\n2012-12-19,2,3.0\n2012-12-12,4,5.0\n2012-12-1,6,10.0")
  let actual, _ = inferType source Int32.MaxValue [||] culture "" false false
  let propDate = { Name = "date"; Optional = false; Type = InferedType.Primitive(typeof<DateTime>, None) }
  let propInt = { Name = "int"; Optional = false; Type = InferedType.Primitive(typeof<int>, None) }
  let propFloat = { Name = "float"; Optional = false; Type = InferedType.Primitive(typeof<Decimal>, None) }
  let expected = InferedType.Record(None, [ propDate ; propInt ; propFloat ])
  actual |> shouldEqual expected

[<Test>]
let ``Inference of DateTime with timestamp``() = 
  let source = CsvFile.Parse("date,timestamp\n2012-12-19,2012-12-19 12:00\n2012-12-12,2012-12-12 00:00\n2012-12-1,2012-12-1 07:00")
  let actual, _ = inferType source Int32.MaxValue [||] culture "" false false
  let propDate = { Name = "date"; Optional = false; Type = InferedType.Primitive(typeof<DateTime>, None) }
  let propTimestamp = { Name = "timestamp"; Optional = false; Type = InferedType.Primitive(typeof<DateTime>, None) }
  let expected = InferedType.Record(None, [ propDate ; propTimestamp ])
  actual |> shouldEqual expected

[<Test>]
let ``Inference of DateTime with timestamp non default separator``() = 
  let source = CsvFile.Parse("date;timestamp\n2012-12-19;2012-12-19 12:00\n2012-12-12;2012-12-12 00:00\n2012-12-1;2012-12-1 07:00", ";")
  let actual, _ = inferType source Int32.MaxValue [||] culture "" false false
  let propDate = { Name = "date"; Optional = false; Type = InferedType.Primitive(typeof<DateTime>, None) }
  let propTimestamp = { Name = "timestamp"; Optional = false; Type = InferedType.Primitive(typeof<DateTime>, None) }
  let expected = InferedType.Record(None, [ propDate ; propTimestamp ])
  actual |> shouldEqual expected

[<Test>]
let ``Inference of float with #N/A values and non default separator``() = 
  let source = CsvFile.Parse("float;integer\n2.0;2\n#N/A;3\n", ";")
  let actual, _ = inferType source Int32.MaxValue [|"#N/A"|] culture "" false false
  let propFloat = { Name = "float"; Optional = false; Type = InferedType.Primitive(typeof<float>, None) }
  let propInteger = { Name = "integer"; Optional = false; Type = InferedType.Primitive(typeof<int>, None) }
  let expected = InferedType.Record(None, [ propFloat ; propInteger ])
  actual |> shouldEqual expected

[<Test>]
let ``Inference of numbers with empty values``() = 
  let source = CsvFile.Parse("float1,float2,float3,float4,int,float5,float6,date,bool,int64\n1,1,1,1,,,,,,\n2.0,#N/A,,1,1,1,,2010-01-10,yes,\n,,2.0,NA,1,foo,2.0,,,2147483648")
  let actual, typeOverrides = inferType source Int32.MaxValue [|"#N/A"; "NA"; "foo"|] culture "" false false
  let propFloat1 = { Name = "float1"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<decimal>, None)) }
  let propFloat2 = { Name = "float2"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<float>, None)) }
  let propFloat3 = { Name = "float3"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<decimal>, None)) }
  let propFloat4 = { Name = "float4"; Optional = false; Type = InferedType.Primitive(typeof<float>, None) }
  let propInt =    { Name = "int";    Optional = false; Type = WithNull(InferedType.Primitive(typeof<Bit1>, None)) }
  let propFloat5 = { Name = "float5"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<float>, None)) }
  let propFloat6 = { Name = "float6"; Optional = false; Type = WithNull(InferedType.Primitive(typeof<decimal>, None)) }
  let propDate =   { Name = "date";   Optional = false; Type = WithNull(InferedType.Primitive(typeof<DateTime>, None)) }
  let propBool =   { Name = "bool";   Optional = false; Type = WithNull(InferedType.Primitive(typeof<bool>, None)) }
  let propInt64 =  { Name = "int64";  Optional = false; Type = WithNull(InferedType.Primitive(typeof<int64>, None)) }
  let expected = InferedType.Record(None, [ propFloat1; propFloat2; propFloat3; propFloat4; propInt; propFloat5; propFloat6; propDate; propBool; propInt64 ])
  actual |> shouldEqual expected

  // Test second part of the csv inference
  let actual = CsvInference.getFields false actual typeOverrides
  let field name (wrapper:TypeWrapper) typ = PrimitiveInferedProperty.Create(name, typ, wrapper)
  let propFloat1 = field "float1" TypeWrapper.None     typeof<float>
  let propFloat2 = field "float2" TypeWrapper.None     typeof<float>
  let propFloat3 = field "float3" TypeWrapper.None     typeof<float>
  let propFloat4 = field "float4" TypeWrapper.None     typeof<float>
  let propInt =    field "int"    TypeWrapper.Nullable typeof<Bit1>
  let propFloat5 = field "float5" TypeWrapper.None     typeof<float>
  let propFloat6 = field "float6" TypeWrapper.None     typeof<float>
  let propDate =   field "date"   TypeWrapper.Option   typeof<DateTime>
  let propBool =   field "bool"   TypeWrapper.Option   typeof<bool>
  let propInt64 =  field "int64"  TypeWrapper.Nullable typeof<int64>
  let expected = [ propFloat1; propFloat2; propFloat3; propFloat4; propInt; propFloat5; propFloat6; propDate; propBool; propInt64 ]
  actual |> shouldEqual expected

//to be able to test units of measures we have to compare the typenames with strings
let prettyTypeName (t:Type) = 
  t.ToString()
   .Replace("Microsoft.FSharp.Data.UnitSystems.SI.", null)
   .Replace("UnitNames.", null)
   .Replace("UnitSymbols.", null)
   .Replace("System.", null)
   .Replace("[]", null)
   .Replace("[", "<")
   .Replace("]", ">")
   .Replace("String", "string")
   .Replace("Double", "float")
   .Replace("Decimal", "decimal")
   .Replace("Int32", "int")
   .Replace("Int64", "int64")
   .Replace("Boolean", "bool")
   .Replace("DateTime", "date")

[<Test>]
let ``Infers units of measure correctly``() = 

    let source = CsvFile.Parse("String(metre), Float(meter),Date (second),Int\t( Second), Decimal  (watt),Bool(N), Long(N), Unknown (measure)\nxpto, #N/A,2010-01-10,4,3.7, yes,2147483648,2")
    let actual = 
      inferType source Int32.MaxValue [|"#N/A"|] culture "" false false
      ||> CsvInference.getFields false
      |> List.map (fun field -> 
          field.Name, 
          field.RuntimeType, 
          prettyTypeName field.TypeWithMeasure)

    let propString =  "String(metre)"      , typeof<string>  , "string"
    let propFloat =   "Float"              , typeof<float>   , "float<meter>"
    let propDate =    "Date (second)"      , typeof<DateTime>, "date"
    let propInt =     "Int"                , typeof<int>     , "int<second>"
    let propDecimal = "Decimal"            , typeof<decimal> , "decimal<watt>"
    let propBool =    "Bool(N)"            , typeof<bool>    , "bool"
    let propLong =    "Long"               , typeof<int64>   , "int64<N>"
    let propInt2 =    "Unknown (measure)"  , typeof<int>     , "int"
    let expected = [ propString; propFloat; propDate; propInt; propDecimal; propBool; propLong; propInt2 ]

    actual |> shouldEqual expected

[<Test>]
let ``Inference schema override by column name``() = 

  let source = CsvFile.Parse("A (second), B (decimal?), C (float<watt>), float, second, float<N>\n1,1,,1,1,1")
  let actual = 
    inferType source Int32.MaxValue [||] culture "" false false
    ||> CsvInference.getFields false
    |> List.map (fun field -> 
        field.Name, 
        field.RuntimeType, 
        prettyTypeName field.TypeWithMeasure,
        field.TypeWrapper)

  let col1 = "A"       , typeof<int>    , "int<second>", TypeWrapper.None
  let col2 = "B"       , typeof<decimal>, "decimal"    , TypeWrapper.Nullable
  let col3 = "C"       , typeof<float>  , "float<watt>", TypeWrapper.None
  let col4 = "float"   , typeof<int>    , "int"        , TypeWrapper.None
  let col5 = "second"  , typeof<int>    , "int"        , TypeWrapper.None
  let col6 = "float<N>", typeof<int>    , "int"        , TypeWrapper.None
  let expected = [ col1; col2; col3; col4; col5; col6 ]

  actual |> shouldEqual expected

[<Test>]
let ``Inference schema override by parameter``() = 

  let source = CsvFile.Parse(",Foo,,,,\n1,1,1,1,1,")
  let actual = 
    inferType source Int32.MaxValue [||] culture "float,,float?<second>,A(float option),foo,C(float<m>)" false false
    ||> CsvInference.getFields false
    |> List.map (fun field -> 
        field.Name, 
        field.RuntimeType, 
        prettyTypeName field.TypeWithMeasure,
        field.TypeWrapper)

  let col1 = "Column1" , typeof<float>, "float"        , TypeWrapper.None
  let col2 = "Foo"     , typeof<int>  , "int"          , TypeWrapper.None
  let col3 = "Column3" , typeof<float>, "float<second>", TypeWrapper.Nullable
  let col4 = "A"       , typeof<float>, "float"        , TypeWrapper.Option
  let col5 = "foo"     , typeof<int>  , "int"          , TypeWrapper.None
  let col6 = "C"       , typeof<float>, "float<m>"     , TypeWrapper.None
  let expected = [ col1; col2; col3; col4; col5; col6 ]

  actual |> shouldEqual expected
