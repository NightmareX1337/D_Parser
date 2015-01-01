﻿using System;
using D_Parser;
using NUnit.Framework;

namespace Tests
{
	[TestFixture]
	public class IndentationTests
	{
		[Ignore]
		const int TabSize = 4;
		const string TabToSpaceRepresentation = "    ";


		[Test]
		public void TestIndenter()
		{
			TestLastLine("", 0);
			
			TestLastLine(@"
tastyDeleg( (uint a)
			{
asd",6);

			TestLastLine(@"import
std", 1);
			TestLastLine(@"import std.stdio,
	std;", 1);
			TestLastLine(@"import std;
", 0);
			TestLastLine(@"import
", 1);
			TestLastLine(@"import std;
import std;
", 0);
			TestLastLine(@"import std.stdio,
	std.conv;",1);
			TestLastLine(@"import std;
import
", 1);
			TestLastLine(@"import std;
import
	std;", 1);

			TestLastLine(@"
class A
{
	void foo()
	{
	}
	", 1);

			TestLastLine(@"
class A
{
	private:
	int a;
	", 1);

			TestLastLine(@"
class A
{
	private:
	int a;
	int b;
}", 0);

			TestLastLine(@"
class A
{
	private:
	int a;
	public:
	", 1);

			TestLastLine(@"foo();", 0);

			TestLastLine(@"foo();
", 0);

			TestLastLine(@"foo(
	);", 1);
			TestLastLine(@"foo(
	a.lol", 1);
			TestLastLine(@"foo(
	a,
	b", 1);
			TestLastLine(@"foo(a,
	b);", 1);

			TestLastLine(@"foo(
	a)", 1);
			TestLastLine(@"foo(
	b())", 1);
			TestLastLine(@"foo(
", 1);
			TestLastLine(@"foo(
)", 1);

			TestLastLine(@"foo(asdf())
{", 0);
			TestLastLine(@"foo(asdf())
", 1);
			TestLastLine(@"foo(asdf()=b)
", 1);
			TestLastLine(@"foo(asdf)
", 1);
			TestLastLine(@"writeln(34,
	joLol);", 2);
			/* TODO
			TestLastLine(@"writeln,
	lolSecondExpression();",1);*/
			TestLastLine(@"std.stdio.
	writeln(a)", 1);
			TestLastLine(@"std.stdio.
	writeln(a);", 1);
			TestLastLine(@"writeln(
	a());", 1);
			TestLastLine(@"writeln(""Hello Word!""
	);", 1);
			TestLastLine(@"writeln(
	(162*2)%315==9);", 1);

			TestLastLine(@"foo()
{
	asdf;
}
", 0);
			TestLastLine(@"foo()
{
	asdf();", 1);
			TestLastLine(@"foo()
{
	asdf();
	", 1);
			TestLastLine(@"foo()
{
	asdf;
}", 0);
			TestLastLine(@"foo()
{
	asdf;
	asdf;}
", 0);
			TestLastLine(@"foo()
{
	asdf;
	asdf;}", 1);
			TestLastLine(@"foo(){
	a;
	bar();}", 1);
			TestLastLine(@"foo(){
lol(); ger++;
} yeah;", 0);
			TestLastLine(@"foo(){
	lol(); } foo();", 1);
			TestLastLine(@"foo(){
	lol(); } foo();
", 0);
			TestLastLine(@"foo(){
	asdf();
	if(true){
		whynot();
		bar(); }} fooGer();", 2);


			TestLastLine(@"foo()
	if(..)", 1);
			TestLastLine(@"foo()
	if(..)
in", 0);
			TestLastLine(@"foo()
in", 0);
			TestLastLine(@"foo()
out", 0);
			TestLastLine(@"foo()
out(result)", 0);
			TestLastLine(@"foo()
	if(true)
out(result)", 0);
			TestLastLine(@"foo()
out(result){", 0);
			TestLastLine(@"foo()
body", 0);
			TestLastLine(@"void foo(in
", 1);
			TestLastLine(@"void foo(out
", 1);



			TestLastLine(@"foo()
{
	b(
		{
			nestedFoo();", 3);

			TestLastLine(@"foo()
{
	b({
		nestedFoo();
	});
	", 1);

			TestLastLine(@"foo()
{
	bar({asdfCall();});", 1);

			TestLastLine(@"class A:B
{", 0);
			TestLastLine(@"class A:
", 1);
			TestLastLine(@"class A:B
", 1);


			TestLastLine(@"enum A
{", 0);
			TestLastLine(@"enum A
{
", 1);
			TestLastLine(@"enum A
{
	a,
	", 1);
			TestLastLine(@"enum A
{
a= A+
B", 2);
			TestLastLine(@"enum A
{
a,
b=A+B,
c", 1);
			TestLastLine(@"enum A
{
a,
b=A+B,
c,
", 1);
			TestLastLine(@"enum A
{
a,
b=A*B,
c,
d,", 1);
			TestLastLine(@"enum A
{
a,
b
,c", 1);
			TestLastLine(@"enum A
{
a
b,
c,
}", 0);

			TestLastLine(@"enum A
{
	a,b
}

void foo()
{
	", 1);


			TestLastLine(@"if(a)
{", 0);

			TestLastLine(@"if(a)
	a;", 1);
			TestLastLine(@"if(asdf)
", 1);
			TestLastLine(@"if(asdf())
", 1);
			TestLastLine(@"if(asdf()==b)
", 1);
			TestLastLine(@"if(
", 1);


			TestLastLine(@"switch(a)
{
	case:", 1);
			TestLastLine(@"switch(a)
{
	case 3:
		lol;", 2);
			TestLastLine(@"switch(a)
{
	case 3:
		lol;
		", 2);

			TestLastLine(@"switch(a)
{
	case 3:
		lol;
}", 0);

			TestLastLine(@"switch(a)
{
	case 3:
		lol;
}
", 0);
			TestLastLine(@"switch(a)
{
	case 3:
		lol;
		asdf;", 2);
			TestLastLine(@"switch(a)
{
	case 3:
		lol;
	case 4:
		asdf;", 2);
			TestLastLine(@"switch(a)
{
	case 3:
		lol;
	default:
	case 4:
		asdf;", 2);


			TestLastLine(@"private:
	", 1);

			TestLastLine(@"version(Windows):
	", 1);
			TestLastLine(@"version(D):", 0);

			TestLastLine(@"
private foo()
{
	a;
}", 0);

			TestLastLine(@"
private foo()
{
	a;
}", 0);

			TestLastLine(@"
private:
	foo()
	{", 1);

			TestLastLine(@"
void main(string[] args)
{
    if(true)
    {
		for(int i=0; i<5;i++)
		{
			i = i % 4;
			if(i == 3)
			{
				i++;
				", 4);

			TestLastLine(@"
void main(string[] args)
{
    if(true)
    {
		for(int i=0; i<5;i++)
		{
			i = i % 4;
			if(i == 3)
			{
				i++;
			}", 3);

			TestLine(@"
void main(string[] args)
{
    if(true)
    {
		for(int i=0; i<5;i++)
		{
			i = i % 4;
			if(i == 3)
			{
				i++;
			}
		}
	}
}", 12, 3);

			TestLastLine(@"
void main(string[] args)
{
    if(true)
    {
		for(int i=0; i<5;i++)
		{
			i = i % 4;
			if(i == 3)
			{
				i++;
			}}", 3);

			TestLastLine(@"
void main(string[] args)
{
    if(true)
    {
		for(int i=0; i<5;i++)
		{
			i = i % 4;
			if(i == 3)
			{
				i++;
				;;", 4);

			TestLine(@"import std.stdio;
void main(string[] args)
{
	writeln();
	
}
", 5, 1);
		}

		/// <summary>
		/// https://github.com/aBothe/Mono-D/issues/576
		/// </summary>
		[Test]
		public void TestIssue576()
		{
			TestLastLine(@"void foo() {
    tastyDeleg(
               asd",4+11);

			TestLastLine(@"
tastyDeleg(
asd",11);
		}

		void TestLastLine(string code, int targetIndent)
		{
			var newInd = GetLastLineIndent(code);
			Assert.AreEqual(targetIndent , newInd, code);

			newInd = GetLastLineIndent(code, true, true);
			Assert.AreEqual(targetIndent, newInd, "[Additional Content]\n"+code);
		}

		void TestLine(string code, int line, int targetIndent)
		{
			var caret = DocumentHelper.OffsetToLocation(code, code.Length);
			var newInd = GetLineIndent(code, line);
			Assert.AreEqual(newInd, targetIndent, code);
		}


		static int GetLineIndent(string code, int line)
		{
			return D_Parser.Formatting.Indent.IndentEngineWrapper.CalculateIndent(code, line).Replace("\t",TabToSpaceRepresentation).Length;
		}

		static int GetLastLineIndent(string code, bool AddNewLines = false, bool AddSomeFinalCode = false)
		{
			var caret = DocumentHelper.OffsetToLocation(code, code.Length);

			if (AddNewLines)
				code += "\r\n\r\n";

			if (AddSomeFinalCode)
				code += "StaticFinalContent;";

			return GetLineIndent(code, caret.Line);
		}
	}
}
