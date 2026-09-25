#!/usr/bin/env python3
"""Export a Tropico 6 .t6sav to decoded_raw.bin, strings.txt, buildings.csv, economy.json, population.json,
island_summary.json (+ building_classes.csv, object_table.csv, decode.log).  Never writes to the source save.

usage: python tools/t6sav_export.py [save.t6sav] [--out out] [--objects]
default save: newest *.t6sav in Documents/My Games/Tropico6/Saved/SaveGames, ignoring Trop6_Profile.t6sav
"""
import sys, os, re, csv, json, glob, logging, argparse, collections, struct

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from t6sav_decode import Save

log = logging.getLogger("t6sav")
U = "unknown"


def newest_save():
    d = os.path.expanduser(r"~\Documents\My Games\Tropico6\Saved\SaveGames")
    fs = [f for f in glob.glob(os.path.join(d, "*.t6sav")) if os.path.basename(f) != "Trop6_Profile.t6sav"]
    if not fs: raise SystemExit("no save found in " + d)
    return max(fs, key=os.path.getmtime)


def short(path):
    return path.split('/')[-1].split('.')[-1]


def last_entry(hist):
    """T6HistoricalData -> last {'ValueX','ValuesY'} entry"""
    try: return hist["Entries"][-1]
    except Exception: return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("save", nargs="?")
    ap.add_argument("--out", default="out")
    ap.add_argument("--objects", action="store_true", help="also write decoded_objects.jsonl (all decoded properties)")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s",
                        handlers=[logging.FileHandler(os.path.join(a.out, "decode.log"), "w", "utf-8"), logging.StreamHandler()])
    path = a.save or newest_save()
    log.info("save: %s (%d bytes)", path, os.path.getsize(path))
    s = Save(path)
    d = s.d
    log.info("header (%d B): %s", len(s.header), s.header[:64])
    log.info("zlib stream @0x%x len %d -> %d bytes", s.zoff, s.zlen, len(d))
    open(os.path.join(a.out, "decoded_raw.bin"), "wb").write(d)

    # ---- strings.txt -----------------------------------------------------------
    with open(os.path.join(a.out, "strings.txt"), "w", encoding="utf-8") as f:
        f.write("# offsets are hex offsets in decoded_raw.bin\n# --- name table (index, offset-independent FName strings)\n")
        for i, n in enumerate(s.names): f.write("N%d\t%s\n" % (i, n))
        f.write("# --- ASCII strings >= 6 chars (object-table paths and native strings)\n")
        for m in re.finditer(rb'[\x20-\x7e]{6,}', d[:s.base]):
            f.write("0x%08x\tA\t%s\n" % (m.start(), m.group().decode()))
        f.write("# --- UTF-16LE strings >= 6 chars\n")
        for m in re.finditer(rb'(?:[\x20-\x7e]\x00){6,}', d):
            f.write("0x%08x\tW\t%s\n" % (m.start(), m.group().decode('utf-16le')))

    # ---- decode all objects ---------------------------------------------------
    R = s.recs
    stat = collections.Counter()
    for r in R:
        r['props'], r['status'], r['tr'] = s.decode_object(r)
        stat[r['status'].split(':')[0].split('@')[0][:12]] += 1
    log.info("decode status: %s", dict(stat))
    comp = collections.defaultdict(list)          # owner idx -> component records (kind1/2, flag 1, x = owner)
    for r in R:
        if r['x'] is not None and r['flag'] == 1: comp[r['x']].append(r)

    def vals(r):
        out = collections.OrderedDict()
        for p in r['props']:
            k = p['name'] + ("[%d]" % p['aidx'] if p['aidx'] else "")
            out.setdefault(k, s.value(p))
        return out
    for r in R: r['v'] = vals(r)
    if a.objects:
        with open(os.path.join(a.out, "decoded_objects.jsonl"), "w", encoding="utf-8") as f:
            for r in R:
                f.write(json.dumps(dict(idx=r['idx'], path=r['path'], kind=r['kind'], flag=r['flag'], owner=r['x'], status=r['status'],
                                        blob="0x%x" % r['start'] if r.get('start') else None, transform=r['tr'], props=r['v']),
                                   default=str) + "\n")
    with open(os.path.join(a.out, "object_table.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f); w.writerow(["idx", "table_offset", "kind", "flag", "owner_idx", "blob_offset", "blob_size", "status", "path"])
        for r in R:
            w.writerow([r['idx'], "0x%x" % r['at'], r['kind'], r['flag'], r['x'] if r['x'] is not None else "",
                        "0x%x" % r['start'] if r.get('start') else "", r['end'] - r['start'] if r.get('start') else "", r['status'], r['path']])

    # ---- classification: class reference vs instance -------------------------
    def classify(r):
        p = r['path']
        if r['kind'] == 3: return "class_reference"          # '#/Game/...' soft class/data-asset refs, no blob
        if p.startswith('/Script/'): return "component_or_native_object"
        if r['flag'] == 1 and r['x'] is not None and r['x'] >= 0 and r['tr'] is None: return "subobject"
        if r['tr'] is not None or r['flag'] == 2: return "actor_instance"
        return "other"
    for r in R: r['class'] = classify(r)

    def is_building(r):
        return '/Buildings/' in r['path'] and r['class'] == "actor_instance" and r['flag'] == 2 and comp.get(r['idx']) \
            and not re.search(r'Visualization|Freighter', r['path'])

    B = [r for r in R if is_building(r)]
    obj_name = lambda ref: short(R[ref['obj']]['path']) if isinstance(ref, dict) and 'obj' in ref and 0 <= ref['obj'] < len(R) else ""
    rows = []
    for r in B:
        cs = comp[r['idx']]
        cnames = [short(c['path']) for c in cs]
        site = next((c for c in cs if short(c['path']) == 'T6ConstructionSiteComponent'), None)
        stocks = []
        for c in cs:
            for st in comp.get(c['idx'], []):
                if short(st['path']) == 'T6Stock':
                    stocks.append({"resource": st['v'].get('ResourceType'), "type": st['v'].get('StockType'),
                                   "size": st['v'].get('CurrentSize'), "capacity": st['v'].get('Capacity')})
        rows.append(dict(class_name=short(r['path']), blueprint_path=r['path'], instance_id=r['idx'], count=1,
                         x=r['tr']['x'] if r['tr'] else U, y=r['tr']['y'] if r['tr'] else U, z=r['tr']['z'] if r['tr'] else U,
                         yaw_uncertain=r['tr']['yaw'] if r['tr'] else U,
                         active=U, workers=U, worker_capacity=U, housing_capacity=U, residents=U,
                         budget=r['v'].get('BudgetLevel', "default(not serialized)"),
                         workmode=obj_name(r['v'].get('CurrentWorkmode')) or U,
                         building_state=site['v'].get('BuildingState', "default(not serialized)") if site else U,
                         construction_progress=site['v'].get('progress', "default(not serialized)") if site else U,
                         has_residence_component=any(n == 'T6ResidenceComponent' for n in cnames),
                         has_workplace_component=any('Workplace' in n for n in cnames),
                         components=";".join(sorted(set(cnames))), stocks=json.dumps(stocks) if stocks else "",
                         blob_offset="0x%x" % r['start'], decode_status=r['status']))
    cols = list(rows[0].keys()) if rows else []
    with open(os.path.join(a.out, "buildings.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, cols); w.writeheader(); w.writerows(rows)
    log.info("buildings.csv: %d instances", len(rows))

    # class table: text occurrences vs record kinds vs instances
    txt = collections.Counter(m.group(0).decode() for m in re.finditer(rb'/Game/Blueprints/Buildings/[\w/]+\.\w+_C', d))
    per = collections.defaultdict(lambda: collections.Counter())
    for r in R:
        if '/Buildings/' in r['path']: per[r['path']][r['class']] += 1
    with open(os.path.join(a.out, "building_classes.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["blueprint_path", "text_occurrences_in_decoded_stream(NOT an instance count)", "object_table_records",
                    "class_reference_records", "actor_instance_records", "counted_building_instances"])
        inst = collections.Counter(r['blueprint_path'] for r in rows)
        for p in sorted(set(per) | set(txt)):
            w.writerow([p, txt.get(p, 0), sum(per[p].values()), per[p].get("class_reference", 0), per[p].get("actor_instance", 0), inst.get(p, 0)])

    # ---- DataCollector (economy / population history) ------------------------
    dc = next((r for r in R if r['path'].endswith('T6DataCollector')), None)
    V = dc['v'] if dc else {}
    def lastv(name, k=0):
        e = last_entry(V.get(name, {}))
        return (e['ValuesY'][k] if e and len(e['ValuesY']) > k else None), (e['ValueX'] if e else None)
    def series(name):
        h = V.get(name);  return [(e['ValueX'], e['ValuesY']) for e in h['Entries']] if isinstance(h, dict) and 'Entries' in h else []
    def sum_field(month_list, key, sub):
        t = 0
        for m in month_list or []:
            x = m.get(key)
            if isinstance(x, list): t += sum(i.get(sub, 0) for i in x)
        return t
    rev = V.get('RevenueLastYear') if isinstance(V.get('RevenueLastYear'), list) else []
    exp = V.get('ExpenseLastYear') if isinstance(V.get('ExpenseLastYear'), list) else []
    def by_res(key):
        o = collections.Counter()
        for m in rev if key == 'Exports' else exp:
            for i in m.get(key, []) if isinstance(m.get(key), list) else []:
                o[i.get('resource', '?')] += i.get('Revenue', i.get('Expenses', 0))
        return dict(o)
    rev_tot = {k: sum_field(rev, k, 'Revenue') for k in ('Exports', 'Fees', 'Rents', 'TouristFees', 'TouristRents')}
    rev_tot['CustomMiscs'] = sum_field(rev, 'CustomMiscs', 'Value')
    rev_tot['SuperpowerAid'] = sum(v for m in rev for k, v in m.items() if k.startswith('SuperpowerAid') and isinstance(v, (int, float)))
    exp_tot = {k: sum_field(exp, k, 'Expenses') for k in ('Imports', 'Constructions', 'Upkeeps', 'Wages')}
    exp_tot['CustomMiscs'] = sum_field(exp, 'CustomMiscs', 'Value')
    exp_tot['CelebWages'] = sum(v for m in exp for k, v in m.items() if k.startswith('CelebWages') and isinstance(v, (int, float)))
    mb = [m.get('Value') for m in V.get('MonthBalance', [])] if isinstance(V.get('MonthBalance'), list) else []
    treas, tx = lastv('TreasuryHistory')
    wages_by_bld = collections.Counter(); upk_by_bld = collections.Counter()
    for m in exp:
        for i in m.get('Wages', []) if isinstance(m.get('Wages'), list) else []: wages_by_bld[obj_name(i.get('building')) or "?"] += i.get('Expenses', 0)
        for i in m.get('Upkeeps', []) if isinstance(m.get('Upkeeps'), list) else []: upk_by_bld[obj_name(i.get('building')) or "?"] += i.get('Expenses', 0)
    routes = []
    for r in R:
        if r['path'].endswith('T6TradeRoute'):
            v = r['v']; routes.append(dict(idx=r['idx'], resource=v.get('resource'), is_import=v.get('bIsImport', "default(false?)"),
                                          volume_goal=v.get('VolumeGoal'), partner=obj_name(v.get('TradePartner')) or v.get('TradePartner')))
    active = [dict(idx=r['idx'], route=r['v'].get('tradeRoute'), current_volume=r['v'].get('CurrentVolume'), start_day=r['v'].get('StartDay'),
                   end_day=r['v'].get('EndDay'), end_reason=r['v'].get('EndReason')) for r in R if r['path'].endswith('T6ActiveTradeRoute')]
    econ = dict(
        _note="Values come from the T6DataCollector object (obj #%s). ValueX is the game-time index of history samples (unit unverified)." % (dc['idx'] if dc else U),
        treasury=dict(value=treas, sample_x=tx, confidence="probable (last TreasuryHistory sample)"),
        swiss_bank=dict(value=lastv('SwissHistory')[0], confidence="probable (last SwissHistory sample; 0 = no account or empty)"),
        last_balance_sample=dict(values=lastv('BalanceHistory')[0:1] and (last_entry(V.get('BalanceHistory', {})) or {}).get('ValuesY'),
                                 confidence="uncertain (2 series in Y: probably revenue, expense)"),
        month_balance_last12=mb, month_counter_raw=V.get('MonthCounter'),
        yearly_income=dict(value=sum(rev_tot.values()), breakdown=rev_tot, confidence="uncertain (sum of RevenueLastYear 12 buckets; does not reconcile exactly with MonthBalance, see check_month_balance_sum)"),
        yearly_expenses=dict(value=sum(exp_tot.values()), breakdown=exp_tot, confidence="uncertain (sum of ExpenseLastYear 12 buckets; see check_month_balance_sum)"),
        check_month_balance_sum=dict(sum_of_MonthBalance=sum(v for v in mb if isinstance(v, (int, float))),
                                     income_minus_expenses=sum(rev_tot.values()) - sum(exp_tot.values())),
        wages=dict(total_last_year=exp_tot['Wages'], by_building_class=dict(wages_by_bld.most_common())),
        maintenance=dict(total_last_year=exp_tot['Upkeeps'], by_building_class=dict(upk_by_bld.most_common())),
        imports=dict(expense_by_resource_last_year=by_res('Imports')),
        exports=dict(revenue_by_resource_last_year=by_res('Exports'), export_income_ints_raw=[p for p in [s.value(x) for x in (dc['props'] if dc else []) if x['name'] == 'ExportIncome']],
                     export_income_note="24 raw ints, resource order not decoded -> uncertain"),
        trade_routes=routes, active_trade_routes=active,
        electricity_production=U, electricity_consumption=U,
        electricity_note="no serialized production/consumption field found; %d T6ElectricityConsumerComponent objects exist" %
                         sum(1 for r in R if r['path'].endswith('T6ElectricityConsumerComponent')),
        histories={k: series(k) for k in ('TreasuryHistory', 'BalanceHistory', 'RevenueHistory', 'ExpenseHistory', 'SwissHistory')})
    json.dump(econ, open(os.path.join(a.out, "economy.json"), "w", encoding="utf-8"), indent=1, default=str)

    # ---- agents ---------------------------------------------------------------
    ag = [r for r in R if r['path'].endswith('Tropico6.T6Agent')]
    life = collections.Counter(); edu = collections.Counter(); eth = collections.Counter(); hap = collections.Counter(); thoughts = collections.Counter()
    hv = []
    for r in ag:
        v = r['v']
        life[v.get('LifeState', "default(not serialized)")] += 1
        edu[v.get('education', "default(not serialized)")] += 1
        eth[v.get('Ethnicity', "default(not serialized)")] += 1
        hap[v.get('HappinessLevel', "default(not serialized)")] += 1
        for t in v.get('AgentThoughts', []) if isinstance(v.get('AgentThoughts'), list) else []: thoughts[t] += 1
        if isinstance(v.get('CurrentHappiness'), (int, float)): hv.append(v['CurrentHappiness'])
    unemp = lastv('UnemployedHistory'); une = last_entry(V.get('UnemployedHistory', {}))
    homeless = last_entry(V.get('HomelessFamiliesByWealthHistory', {})); vac = last_entry(V.get('VacantHomesByWealthNumberHistory', {}))
    slots = last_entry(V.get('HouseholdSlotsHistory', {})); openj = last_entry(V.get('OpenJobsHistory', {}))
    workers = last_entry(V.get('WorkersHistory', {})); edudist = last_entry(V.get('AgentEducationDistributionHistory', {}))
    g = lambda k: V.get(k, U)
    uy = (une or {}).get('ValuesY') or []
    pop = dict(
        total_population=dict(value=g('TotalCitizensNum'), confidence="probable (T6DataCollector.TotalCitizensNum)"),
        breakdown=dict(children=g('ChildrenNum'), adults=g('AdultsNum'), retired=g('RetiredNum'), prisoners=g('PrisonersNum'),
                       soldiers=g('SoldiersNum'), voters=g('VotersNum'), native_tropicans=g('NativeTropicanNum'), immigrants=g('ImmigrantsNum')),
        agent_objects_in_save=len(ag), agent_objects_note="T6Agent records; can exceed TotalCitizensNum (includes children/prisoners/non-counted agents?) -> uncertain",
        unemployed_total=dict(value=sum(uy) if uy else U, confidence="uncertain (sum of last UnemployedHistory sample)"),
        unemployed_uneducated=uy[0] if len(uy) > 0 else U, unemployed_high_school=uy[1] if len(uy) > 1 else U,
        unemployed_college=uy[2] if len(uy) > 2 else U,
        unemployed_series_note="3 Y-series assumed = education levels (uneducated, high school, college), by analogy with AgentEducationDistributionHistory (3 series); UNVERIFIED against the in-game Almanac",
        homeless_families=dict(last_sample_by_wealth=(homeless or {}).get('ValuesY'), sum=sum((homeless or {}).get('ValuesY') or []) or U,
                               confidence="uncertain (HomelessFamiliesByWealthHistory, variable-length Y array: trailing zeros probably omitted)"),
        homeless_citizens=U, occupied_homes=U, total_homes=U,
        vacant_homes_by_wealth_last_sample=(vac or {}).get('ValuesY'),
        household_slots_last_sample=dict(values=(slots or {}).get('ValuesY'), confidence="unknown meaning"),
        available_jobs=dict(open_jobs_last_sample=(openj or {}).get('ValuesY'), confidence="uncertain"), total_jobs=U,
        workers_history_last_sample=dict(values=(workers or {}).get('ValuesY'), confidence="unknown meaning"),
        education_distribution_last_sample=dict(values=(edudist or {}).get('ValuesY'), confidence="uncertain: probably [uneducated, high school, college] incl. children"),
        agent_life_state=dict(life), agent_education=dict(edu), agent_ethnicity=dict(eth), agent_happiness_level=dict(hap),
        agent_thoughts=dict(thoughts.most_common(15)),
        agent_note="Agent blobs are only PARTIALLY decoded (props before the first undecodable native segment); missing enum = default value not serialized",
        history_last_sample_x=(une or {}).get('ValueX'))
    json.dump(pop, open(os.path.join(a.out, "population.json"), "w", encoding="utf-8"), indent=1, default=str)

    # ---- resources / deposits ---------------------------------------------------
    stock = collections.defaultdict(lambda: dict(size=0.0, capacity=0.0, n=0))
    for r in R:
        if r['path'].endswith('Tropico6.T6Stock'):
            v = r['v']; k = v.get('ResourceType', "default(not serialized)")
            stock[k]['size'] += v.get('CurrentSize', 0.0) if isinstance(v.get('CurrentSize'), (int, float)) else 0
            stock[k]['capacity'] += v.get('Capacity', 0.0) if isinstance(v.get('Capacity'), (int, float)) else 0
            stock[k]['n'] += 1
    dep = collections.defaultdict(lambda: dict(n=0, remaining=0.0))
    for r in R:
        if 'ResourceDeposits' in r['path'] and r['kind'] != 3:
            k = short(r['path']); dep[k]['n'] += 1; dep[k]['remaining'] += r['v'].get('RemainingResourceAmount', 0) or 0
    bcount = collections.Counter(r['class_name'] for r in rows)
    state = collections.Counter(r['building_state'] for r in rows)
    summ = dict(
        source_save=os.path.basename(path), game_build=s.header[8:8 + 27].decode('latin1', 'replace'),
        zlib_offset=hex(s.zoff), decoded_bytes=len(d), names=len(s.names), objects=len(R),
        object_kinds=dict(collections.Counter(r['class'] for r in R)),
        decode_status=dict(stat),
        year=g('Year') if False else next((r['v'].get('Year') for r in R if r['path'].endswith('T6Calendar')), U),
        buildings=dict(instances=len(rows), by_class=dict(bcount.most_common()), by_state=dict(state),
                       note="instance = object-table record under /Game/Blueprints/Buildings/ with an actor blob (flag 2) that owns components; "
                            "class references (kind 3, '#/Game/...DA_*') and visualization helpers are excluded. Text occurrence counts are NOT used."),
        population=dict(total=pop['total_population']['value'], breakdown=pop['breakdown'], unemployed=pop['unemployed_total']),
        economy=dict(treasury=econ['treasury']['value'], yearly_income=econ['yearly_income']['value'],
                     yearly_expenses=econ['yearly_expenses']['value'], swiss_bank=econ['swiss_bank']['value']),
        stocks_by_resource={k: dict(v) for k, v in stock.items()}, resource_deposits={k: dict(v) for k, v in dep.items()},
        trade_routes_count=len(routes), active_trade_routes_count=len(active),
        unknown_fields=["workers per building", "worker_capacity", "housing_capacity", "residents", "homes occupied/total", "jobs total",
                        "electricity", "building active flag", "agent -> home/workplace links"],
        files=["decoded_raw.bin", "strings.txt", "object_table.csv", "buildings.csv", "building_classes.csv", "economy.json",
               "population.json", "island_summary.json", "decode.log"])
    json.dump(summ, open(os.path.join(a.out, "island_summary.json"), "w", encoding="utf-8"), indent=1, default=str)
    log.info("done -> %s", os.path.abspath(a.out))


if __name__ == "__main__":
    main()
