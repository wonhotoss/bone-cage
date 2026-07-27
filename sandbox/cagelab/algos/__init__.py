from . import v1_tubes, v2_pipe

REGISTRY = {a.name: a for a in [v1_tubes.algo(), v2_pipe.algo()]}


def get(name):
    if name in REGISTRY:
        return REGISTRY[name]
    # "v2_pipe:margin=1.1,torso_subdiv=3" builds a parameter variant on the fly.
    base, _, args = name.partition(':')
    kw = {}
    for part in filter(None, args.split(',')):
        k, _, v = part.partition('=')
        kw[k] = float(v) if '.' in v else int(v)
    return {'v1_tubes': v1_tubes, 'v2_pipe': v2_pipe}[base].algo(**kw)
