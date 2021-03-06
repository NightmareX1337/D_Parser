﻿using System.Linq;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.ExpressionSemantics;
using NUnit.Framework;

namespace Tests.Resolution
{
	[TestFixture]
	public class MixinResolutionTests : ResolutionTestHelper
	{
		[Test]
		public void MixinCache()
		{
			var ctxt = CreateCtxt("A", @"module A;

mixin(""int intA;"");

class ClassA
{
	mixin(""int intB;"");
}

class ClassB(T)
{
	mixin(""int intC;"");
}

ClassA ca;
ClassB!int cb;
ClassB!bool cc;
");

			IExpression x, x2;
			MemberSymbol t, t2;

			x = DParser.ParseExpression("intA");
			t = ExpressionTypeEvaluation.EvaluateType(x, ctxt) as MemberSymbol;
			t2 = ExpressionTypeEvaluation.EvaluateType(x, ctxt) as MemberSymbol;

			Assert.That(t.Definition, Is.SameAs(t2.Definition));

			x = DParser.ParseExpression("ca.intB");
			t = ExpressionTypeEvaluation.EvaluateType(x, ctxt) as MemberSymbol;
			t2 = ExpressionTypeEvaluation.EvaluateType(x, ctxt) as MemberSymbol;

			Assert.That(t.Definition, Is.SameAs(t2.Definition));

			x = DParser.ParseExpression("cb.intC");
			t = ExpressionTypeEvaluation.EvaluateType(x, ctxt) as MemberSymbol;
			t2 = ExpressionTypeEvaluation.EvaluateType(x, ctxt) as MemberSymbol;

			Assert.That(t.Definition, Is.SameAs(t2.Definition));

			x2 = DParser.ParseExpression("cc.intC");
			t2 = ExpressionTypeEvaluation.EvaluateType(x2, ctxt) as MemberSymbol;

			Assert.That(t.Definition, Is.Not.SameAs(t2.Definition));
		}

		[Test]
		public void Mixins1()
		{
			var pcl = ResolutionTests.CreateCache(@"module A;
private mixin(""int privA;"");
package mixin(""int packA;"");
private int privAA;
package int packAA;

mixin(""int x; int ""~""y""~"";"");",

												  @"module pack.B;
import A;",
												 @"module C; import A;");

			var ctxt = ResolutionTests.CreateDefCtxt(pcl, pcl.FirstPackage()["A"]);

			var x = R("x", ctxt);
			Assert.That(x.Count, Is.EqualTo(1));

			x = R("y", ctxt);
			Assert.That(x.Count, Is.EqualTo(1));

			ctxt.CurrentContext.Set(pcl.FirstPackage()["pack.B"]);

			x = R("x", ctxt);
			Assert.That(x.Count, Is.EqualTo(1));

			x = R("privAA", ctxt);
			Assert.That(x.Count, Is.EqualTo(0));

			x = R("privA", ctxt);
			Assert.That(x.Count, Is.EqualTo(0));

			x = R("packAA", ctxt);
			Assert.That(x.Count, Is.EqualTo(0));

			x = R("packA", ctxt);
			Assert.That(x.Count, Is.EqualTo(0));

			ctxt.CurrentContext.Set(pcl.FirstPackage()["C"]);

			x = R("privA", ctxt);
			Assert.That(x.Count, Is.EqualTo(0));

			x = R("packAA", ctxt);
			Assert.That(x.Count, Is.EqualTo(1));

			x = R("packA", ctxt);
			Assert.That(x.Count, Is.EqualTo(1));
		}

		[Test]
		public void Mixins2()
		{
			var pcl = ResolutionTests.CreateCache(@"module A; 

void main()
{
	mixin(""int x;"");
	
	derp;
	
	mixin(""int y;"");
}
");

			var A = pcl.FirstPackage()["A"];
			var main = A["main"].First() as DMethod;
			var stmt = main.Body.SubStatements.ElementAt(1);
			var ctxt = ResolutionTests.CreateDefCtxt(pcl, main, stmt);

			var t = RS((ITypeDeclaration)new IdentifierDeclaration("x") { Location = stmt.Location }, ctxt);
			Assert.That(t, Is.TypeOf(typeof(MemberSymbol)));

			t = RS((ITypeDeclaration)new IdentifierDeclaration("y") { Location = stmt.Location }, ctxt);
			Assert.That(t, Is.Null);
		}

		[Test]
		public void Mixins3()
		{
			var ctxt = ResolutionTests.CreateDefCtxt(@"module A;
template Temp(string v)
{
	mixin(v);
}

class cl
{
	mixin(""int someInt=345;"");
}");
			IExpression ex;
			AbstractType t;

			ex = DParser.ParseExpression("(new cl()).someInt");
			t = ExpressionTypeEvaluation.EvaluateType(ex, ctxt);
			Assert.That(t, Is.InstanceOf(typeof(MemberSymbol)));

			ex = DParser.ParseExpression("Temp!\"int Temp;\"");
			t = ExpressionTypeEvaluation.EvaluateType(ex, ctxt);
			Assert.That(t, Is.InstanceOf(typeof(MemberSymbol)));
		}

		[Test]
		public void Mixins4()
		{
			var pcl = ResolutionTests.CreateCache(@"module A; enum mixinStuff = q{import C;};",
												  @"module B; import A; mixin(mixinStuff); class cl{ void bar(){  } }",
												  @"module C; void CFoo() {}");

			var B = pcl.FirstPackage()["B"];
			var ctxt = ResolutionTests.CreateDefCtxt(pcl, B);

			var t = RS("CFoo", ctxt);
			Assert.That(t, Is.TypeOf(typeof(MemberSymbol)));
			Assert.That((t as MemberSymbol).Definition, Is.TypeOf(typeof(DMethod)));

			var bar = (B["cl"].First() as DClassLike)["bar"].First() as DMethod;
			ctxt.CurrentContext.Set(bar, bar.Body.Location);

			t = RS("CFoo", ctxt);
			Assert.That(t, Is.TypeOf(typeof(MemberSymbol)));
			Assert.That((t as MemberSymbol).Definition, Is.TypeOf(typeof(DMethod)));
		}

		[Test]
		public void Mixins5()
		{
			var pcl = ResolutionTests.CreateCache(@"module A;
mixin(""template mxT(string n) { enum mxT = n; }"");
mixin(""class ""~mxT!(""myClass"")~"" {}"");
", @"module B;
mixin(""class ""~mxT!(""myClass"")~"" {}"");
mixin(""template mxT(string n) { enum mxT = n; }"");
");

			var ctxt = ResolutionTests.CreateDefCtxt(pcl, pcl.FirstPackage()["A"]);

			var t = RS("myClass", ctxt);
			Assert.That(t, Is.TypeOf(typeof(ClassType)));

			ctxt.CurrentContext.Set(pcl.FirstPackage()["B"]);

			t = RS("myClass", ctxt);
			Assert.That(t, Is.Null);
		}

		[Test]
		public void StaticProperty_Stringof()
		{
			var ctxt = CreateCtxt("A", @"module A;
interface IUnknown {}

public template uuid(T, immutable char[] g) {
	const char [] uuid =
		""const IID IID_""~T.stringof~""={ 0x"" ~ g[0..8] ~ "",0x"" ~ g[9..13] ~ "",0x"" ~ g[14..18] ~ "",[0x"" ~ g[19..21] ~ "",0x"" ~ g[21..23] ~ "",0x"" ~ g[24..26] ~ "",0x"" ~ g[26..28] ~ "",0x"" ~ g[28..30] ~ "",0x"" ~ g[30..32] ~ "",0x"" ~ g[32..34] ~ "",0x"" ~ g[34..36] ~ ""]};""
		""template uuidof(T:""~T.stringof~""){""
		""    const IID uuidof ={ 0x"" ~ g[0..8] ~ "",0x"" ~ g[9..13] ~ "",0x"" ~ g[14..18] ~ "",[0x"" ~ g[19..21] ~ "",0x"" ~ g[21..23] ~ "",0x"" ~ g[24..26] ~ "",0x"" ~ g[26..28] ~ "",0x"" ~ g[28..30] ~ "",0x"" ~ g[30..32] ~ "",0x"" ~ g[32..34] ~ "",0x"" ~ g[34..36] ~ ""]};""
		""}"";
}
");

			IExpression x;
			ISymbolValue v;

			x = DParser.ParseExpression(@"uuid!(IUnknown, ""00000000-0000-0000-C000-000000000046"")");
			(x as TemplateInstanceExpression).Location = new CodeLocation(1, 3);
			v = D_Parser.Resolver.ExpressionSemantics.Evaluation.EvaluateValue(x, ctxt);

			var av = v as ArrayValue;
			Assert.That(v, Is.TypeOf(typeof(ArrayValue)));
			Assert.That(av.IsString);
			Assert.That(av.StringValue, Is.EqualTo(
				@"const IID IID_IUnknown={ 0x00000000,0x0000,0x0000,[0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46]};"
				+ "template uuidof(T:IUnknown){"
				+ "    const IID uuidof ={ 0x00000000,0x0000,0x0000,[0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46]};"
				+ "}"));
		}

		[Test]
		public void Mixins7()
		{
			var ctxt = CreateCtxt("A", @"module A;
mixin template mix_test() {int a;}

class C {
enum mix = ""test"";
mixin( ""mixin mix_"" ~ mix ~ "";"" );
}

C c;
");
			IExpression x;
			AbstractType t;

			x = DParser.ParseExpression("c.a");
			t = ExpressionTypeEvaluation.EvaluateType(x, ctxt);

			Assert.That(t, Is.TypeOf(typeof(MemberSymbol)));
			Assert.That((t as MemberSymbol).Base, Is.TypeOf(typeof(PrimitiveType)));
		}

		[Test]
		public void NestedMixins()
		{
			var pcl = CreateCache(@"module A;
mixin(""template mxT1(string n) { enum mxT1 = n; }"");
mixin(mxT1!(""template"")~"" mxT2(string n) { enum mxT2 = n; }"");
mixin(""template mxT3(string n) { ""~mxT2!(""enum"")~"" mxT3 = n; }"");

mixin(""template mxT4(""~mxT3!(""string"")~"" n) { enum mxT4 = n; }"");
mixin(""class ""~mxT4!(""myClass"")~"" {}"");"");");

			var ctxt = CreateDefCtxt(pcl, pcl.FirstPackage()["A"]);

			var t = RS("mxT1", ctxt);
			Assert.That(t, Is.TypeOf(typeof(TemplateType)));

			t = RS("mxT2", ctxt);
			Assert.That(t, Is.TypeOf(typeof(TemplateType)));

			t = RS("mxT3", ctxt);
			Assert.That(t, Is.TypeOf(typeof(TemplateType)));

			t = RS("mxT4", ctxt);
			Assert.That(t, Is.TypeOf(typeof(TemplateType)));

			t = RS("myClass", ctxt);
			Assert.That(t, Is.TypeOf(typeof(ClassType)));
		}
	}
}
