using SourceGeneratorSample;

var mc = new MyClass() { Hoge = 10, Bar = "tako" };
Console.WriteLine(mc); // 出力 : Hoge:10, Bar:tako

[GenerateToString]
public partial class MyClass
{
    public int Hoge { get; set; }
    public string? Bar { get; set; }
}