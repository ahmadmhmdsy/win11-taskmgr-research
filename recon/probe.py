import json, psutil, ctypes, sys
out = {}
out['psutil'] = psutil.__version__
try:
    p = next(psutil.process_iter())
    out['proc_attrs_sample'] = sorted(p.as_dict().keys())
except Exception as e:
    out['proc_err'] = str(e)
attrs = ['pid','name','exe','cmdline','username','cpu_percent','memory_info','num_threads','create_time','status','ppid','nice','io_counters','memory_full_info']
try:
    lst = []
    for pr in list(psutil.process_iter(['pid','name','cpu_percent','memory_info','num_threads','username','exe','ppid','status']))[:5]:
        lst.append(pr.info)
    out['procs'] = lst
except Exception as e:
    out['procs_err'] = str(e)
try:
    svcs = []
    for s in list(psutil.win_service_iter())[:3]:
        d = s.as_dict(); d.pop('binpath', None); svcs.append(d)
    out['services'] = svcs
    out['service_count'] = len(list(psutil.win_service_iter()))
except Exception as e:
    out['services_err'] = str(e)
try:
    out['cpu'] = {'logical': psutil.cpu_count(), 'freq': psutil.cpu_freq()._asdict() if psutil.cpu_freq() else None, 'percpu': psutil.cpu_percent(percpu=True)}
    vm = psutil.virtual_memory(); out['vm'] = vm._asdict()
    sm = psutil.swap_memory(); out['swap'] = sm._asdict()
    out['disk_partitions'] = [p._asdict() for p in psutil.disk_partitions()]
    out['disk_io'] = psutil.disk_io_counters(perdisk=True)
except Exception as e:
    out['cpu_err'] = str(e)
try:
    out['net_io'] = psutil.net_io_counters(pernic=True)
    out['net_if'] = list(psutil.net_if_stats().keys())
except Exception as e:
    out['net_err'] = str(e)
try:
    out['users'] = [u._asdict() for u in psutil.users()]
except Exception as e:
    out['users_err'] = str(e)

def psutil_caps():
    c = {}
    c['cpu_affinity'] = hasattr(psutil.Process, 'cpu_affinity')
    c['nice'] = hasattr(psutil.Process, 'nice')
    c['ionice'] = hasattr(psutil.Process, 'ionice')
    c['memory_full_info'] = hasattr(psutil.Process, 'memory_full_info')
    c['num_handles'] = hasattr(psutil.Process, 'num_handles')
    c['terminate'] = True
    return c
out['caps'] = psutil_caps()

# GUI objects via ctypes
try:
    gdi = ctypes.windll.user32.GetGuiResources
    out['gdi_ok'] = True
except Exception as e:
    out['gdi_err'] = str(e)

print(json.dumps(out, indent=1, default=str)[:6000])
