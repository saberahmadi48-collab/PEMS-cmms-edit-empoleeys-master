"""Remove extractor stub nodes (no source_file, no edges) from graphify-out/graph.json. Run after `graphify update .`."""
import json, sys

path = sys.argv[1] if len(sys.argv) > 1 else "graphify-out/graph.json"
g = json.load(open(path, encoding="utf-8"))
linked = {e["source"] for e in g["links"]} | {e["target"] for e in g["links"]}
keep = [n for n in g["nodes"] if n["id"] in linked or n.get("source_file")]
print(f"nodes {len(g['nodes'])} -> {len(keep)} (removed {len(g['nodes']) - len(keep)} isolated stubs)")
g["nodes"] = keep
json.dump(g, open(path, "w", encoding="utf-8"), ensure_ascii=False)
