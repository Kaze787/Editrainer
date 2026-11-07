# Pairs every form the editor offers with its image name on PokeAPI/sprites.
#   python Map-FormSprites.py <forms.json from "CoreTests --forms"> <listing.json of the repository>
# Writes form-sprites.json, which Download-FormSprites.ps1 reads.
import csv
import json
import re
import sys
from pathlib import Path

here = Path(__file__).parent
forms = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
listing = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
rows = list(csv.DictReader(open(here / "pokemon.csv", encoding="utf-8-sig")))

# The look never changes for these, so the species' own picture is already right.
SAME_LOOK = {664, 665, 414}
# Names that PKHeX and PokeAPI write differently.
RENAMED = {
    (25, "cosplay-4"): "phd", (172, "1"): "spiky-eared", (201, "26"): "exclamation", (201, "27"): "question",
    (649, "1"): "douse", (649, "2"): "shock", (649, "3"): "burn", (649, "4"): "chill",
    (666, "0"): "icy-snow", (666, "19"): "poke-ball", (718, "2"): "10", (741, "2"): "pau", (744, "1"): "own-tempo",
}
# PokeAPI's default picture is not always PKHeX's first form: Vivillon's is Meadow and Maushold's the family of four.
# Zygarde's Power Construct forms look like the plain ones.
DIRECT = {(666, "6"): "666", (718, "3"): "718", (925, "0"): "10257"}
SWEETS = ["strawberry", "berry", "love", "star", "clover", "flower", "ribbon"]
by_species = {}
for row in rows:
    by_species.setdefault(int(row["species_id"]), []).append(row)


def slug(text):
    text = text.replace("é", "e").replace("%", "").replace("'", "").replace("’", "")
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def remote(form):
    species, variant, name = form["species"], form["variant"], form["name"]
    key = (species, variant)
    if key in DIRECT:
        return DIRECT[key]
    if species == 869:
        return f"869-{slug(name)}-{SWEETS[form['Argument']]}-sweet"
    if variant == "0" and key not in RENAMED:
        return None
    text = RENAMED.get(key, slug(re.sub(r"^C-", "", name)))
    direct = f"{species}-{text}"
    if direct + ".png" in listing["artwork"] or direct + ".png" in listing["sprites"]:
        return direct
    candidates = by_species.get(species, [])
    exact = [r for r in candidates if r["identifier"].endswith("-" + text)]
    partial = [r for r in candidates if f"-{text}-" in r["identifier"]]
    return (exact + partial)[0]["id"] if exact or partial else None


table, unmatched = [], []
for form in forms:
    if form["species"] in SAME_LOOK or (form["species"], form["variant"]) == (869, "0-0"):
        continue
    name = remote(form)
    if name is not None:
        table.append({"local": f"{form['species']}-{form['variant']}", "remote": name})
    elif form["variant"] != "0":
        unmatched.append(f"{form['species']}-{form['variant']} {form['name']}")

(here / "form-sprites.json").write_text(json.dumps(table, indent=1), encoding="utf-8")


def size(kind, name, fallback=None):
    file = name + ".png"
    if file in listing[kind]:
        return listing[kind][file], True
    return (listing[fallback][file], True) if fallback and file in listing[fallback] else (0, False)


total = files = 0
for kind, fallback in (("sprites", None), ("sprites_shiny", None), ("artwork", "home"), ("artwork_shiny", "home_shiny")):
    count = weight = 0
    gaps = []
    for entry in table:
        bytes_, found = size(kind, entry["remote"], fallback)
        count += found
        weight += bytes_
        if not found:
            gaps.append(entry["local"])
    print(f"{kind}: {count} de {len(table)}, {weight / 1024 ** 2:.1f} MB; faltan: {gaps[:12]}{' ...' if len(gaps) > 12 else ''}")
    total += weight
    files += count
print(f"TOTAL: {files} archivos, {total / 1024 ** 2:.1f} MB")
print("Sin imagen:", unmatched)
