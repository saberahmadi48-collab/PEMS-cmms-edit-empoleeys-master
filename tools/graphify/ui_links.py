"""Add the UI component registry to the code graph (ADR-0013): a node per UiKeys key, edges key -> component, key -> facade (tag helper) and view -> key.
Run after prune.py (tools/graphify/update.py does). Idempotent: its own nodes carry source 'ui_links'."""
import json, re, sys, pathlib

root = pathlib.Path(__file__).resolve().parents[2]
path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root / "graphify-out" / "graph.json"
g = json.load(open(path, encoding="utf-8"))
g["nodes"] = [n for n in g["nodes"] if n.get("origin") != "ui_links"]
g["links"] = [e for e in g["links"] if e.get("origin") != "ui_links"]

ui = root / "PEMS.UI"
keys = dict(re.findall(r'public const string (\w+) = "([^"]+)"', (ui / "Rendering" / "UiKeys.cs").read_text(encoding="utf-8")))
registry = (ui / "Rendering" / "PemsComponentRegistry.cs").read_text(encoding="utf-8")
component_of = {k: c for k, c in re.findall(r"Register\(UiKeys\.(\w+), typeof\((\w+)\)\)", registry)}

facades = {}   # tag -> (class, kind or key constant, file)
for f in (ui / "TagHelpers").glob("*.cs"):
    text = f.read_text(encoding="utf-8")
    for tag, attr, cls in re.findall(r'\[HtmlTargetElement\("(pems-[a-z-]+)"[^\n]*\]\s*\[PemsFacade\(([^)]*)\)\]\s*public sealed class (\w+)', text):
        facades[tag] = (cls, attr.strip(), f.relative_to(root).as_posix())

ids = {n["id"]: n for n in g["nodes"]}
by_label = {}
for n in g["nodes"]:
    by_label.setdefault(n.get("label"), n)

def node(nid, label, source=None):
    if nid not in ids:
        n = {"id": nid, "label": label, "file_type": "code", "norm_label": label.lower(), "origin": "ui_links", "community_name": "UI component registry"}
        if source: n["source_file"] = source
        g["nodes"].append(n); ids[nid] = n
    return nid

def edge(a, b, rel):
    g["links"].append({"source": a, "target": b, "relation": rel, "confidence": "EXTRACTED", "origin": "ui_links"})

def file_node(rel):
    """the graph's own node for a file, or a new file node"""
    for n in g["nodes"]:
        if (n.get("source_file") or "").replace(chr(92), "/") == rel and n.get("source_location") in (None, "L1"):
            return n["id"]
    return node("file:" + rel, rel, rel)

used = {}   # key -> facade tags
for tag, (cls, attr, f) in facades.items():
    if attr.startswith("UiKeys."):
        used.setdefault(attr[7:], []).append(tag)
for const, value in keys.items():
    k = node("ui_key:" + value, "ui:" + value)
    cls = component_of.get(const)
    if cls:
        comp = ui / "Components" / (cls + ".razor")
        if comp.exists(): edge(k, file_node(comp.relative_to(root).as_posix()), "renders_component")
    for tag in used.get(const, []):
        c, _, f = facades[tag]
        edge(k, file_node(f), "facade_of")

# views -> keys through the tags they write
tag_key = {tag: keys[attr[7:]] for tag, (c, attr, f) in facades.items() if attr.startswith("UiKeys.")}
direct = {tag for tag, (c, attr, f) in facades.items() if "Direct" in attr}
for view in (root / "PEMS.Web" / "Views").rglob("*.cshtml"):
    text = view.read_text(encoding="utf-8", errors="ignore")
    seen = set(re.findall(r"<(pems-[a-z-]+)", text))
    rel = view.relative_to(root).as_posix()
    for tag in seen:
        if tag in tag_key: edge(file_node(rel), "ui_key:" + tag_key[tag], "uses_component")
        elif tag in direct:
            d = node("ui_direct:" + tag, "ui-direct:" + tag)
            edge(file_node(rel), d, "uses_direct_facade")
json.dump(g, open(path, "w", encoding="utf-8"), ensure_ascii=False)
print(f"ui_links: {len(keys)} keys, {len(facades)} facades, {sum(1 for e in g['links'] if e.get('origin') == 'ui_links')} edges")
