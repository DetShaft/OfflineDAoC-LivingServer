extends RefCounted
## Client sounds (content/sounds, .wav), read from disk on a worker thread.
##
## Every sound used to be read on the main thread the first time it played. On the owner's PC one such read took
## 200-600 ms in the sessions where the disk was busy (perf_live.log 2026-11-16: "sound loads" and combat packets held
## a frame 110 times in 46 minutes; the same reads took 0-9 ms on 2026-11-12), and each was a visible hitch. A first
## play now asks for the file and carries on: the frame never waits for the disk.
##
## No class_name: the installer drops this file into a packaged client whose class cache does not list it, so the
## scripts that use it preload it by path.

const DIR := "res://content/sounds"

static var blocking := false               # tests: read on the calling thread, as before (a probe's first play counts)
static var load_us := 0                    # time spent reading sound files, off the main thread (tests/walk_perf.gd)
static var _streams := {}                  # key -> AudioStream (null: the file can't be read)
static var _waiting := {}                  # key -> [Callable]: who wants it once it's read


static func _key(file: String, loop: bool) -> String:
	return file + ("#loop" if loop else "")


## The sound if it has been read (null when it hasn't, or can't be).
static func stream(file: String, loop := false) -> AudioStream:
	return _streams.get(_key(file, loop))


static func has(file: String, loop := false) -> bool:
	return _streams.has(_key(file, loop))


## Calls `then` with the sound (null: unreadable): now if it has been read, else on the main thread once a worker
## thread has read it. A `then` whose object has been freed by then is skipped. `loop`: a copy that loops end to end.
static func want(file: String, then: Callable, loop := false) -> void:
	var key := _key(file, loop)
	if _streams.has(key):
		then.call(_streams[key])
		return
	if blocking:
		_done(key, _read(file, loop), 0)
		then.call(_streams[key])
		return
	if _waiting.has(key):
		(_waiting[key] as Array).append(then)
		return
	_waiting[key] = [then]
	WorkerThreadPool.add_task(_work.bind(file, loop, key), false, "sound " + file)


static func _read(file: String, loop: bool) -> AudioStream:
	var st := AudioStreamWAV.load_from_file(ProjectSettings.globalize_path(DIR.path_join(file)))
	if loop and st != null:
		var frame_bytes := (2 if st.stereo else 1) * 2
		st.loop_mode = AudioStreamWAV.LOOP_FORWARD
		st.loop_begin = 0
		st.loop_end = st.data.size() / frame_bytes
	return st


static func _work(file: String, loop: bool, key: String) -> void:      # (a worker thread)
	var t0 := Time.get_ticks_usec()
	var st := _read(file, loop)
	_done.call_deferred(key, st, Time.get_ticks_usec() - t0)


static func _done(key: String, st: AudioStream, usec: int) -> void:    # (the main thread)
	load_us += usec
	_streams[key] = st
	var waiting: Array = _waiting.get(key, [])
	_waiting.erase(key)
	for then: Callable in waiting:
		if then.is_valid():
			then.call(st)
