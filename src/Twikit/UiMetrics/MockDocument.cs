#pragma warning disable IDE1006 // Naming: these members are called from JavaScript and must keep DOM names.

namespace Twikit.UiMetrics;

/// <summary>ui_metrics スクリプトが触る最小限の DOM 要素モック。</summary>
public sealed class MockElement
{
    public string tagName { get; }
    public MockDocument document { get; }
    public MockElement? parentNode { get; set; }

    public MockElement(string tagName, MockDocument document)
    {
        this.tagName = tagName;
        this.document = document;
    }

    public void appendChild(MockElement child) => child.parentNode = this;

    public void remove() => document.element_seq.Remove(this);

    public void removeChild(MockElement child) => child.remove();

    public MockElement lastElementChild => children[^1];

    public void setAttribute(string name, string value) { }

    public MockElement[] children => document.FilterElements(x => x.parentNode == this);
}

/// <summary>ui_metrics スクリプトが触る最小限の document モック。</summary>
public sealed class MockDocument
{
    public List<MockElement> element_seq { get; } = new();

    public MockDocument()
    {
        createElement("body");
    }

    public MockElement createElement(string tagName)
    {
        var element = new MockElement(tagName, this);
        element_seq.Add(element);
        return element;
    }

    internal MockElement[] FilterElements(Func<MockElement, bool> predicate) => element_seq.Where(predicate).ToArray();

    public MockElement[] getElementsByTagName(string tagName) => FilterElements(x => x.tagName == tagName);
}
