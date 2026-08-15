// Tabbit's data updater.
//
// Brings a local copy of the exported data up to date with a copy served over HTTP - a
// CDN, a bucket, a patch server - so a running program can take new data without being
// rebuilt. Emitted beside the reader and reads nothing but the manifest, so it knows
// nothing about the schema and never has to change when one does.
//
// The manifest is what the exporter already writes next to the data: one entry per file
// with its size and MD5. Comparing it with the local copy is the whole of the diff, so a
// run downloads what changed and nothing else.
//
// Three properties, because a patcher that fails badly is worse than one that does not
// exist:
//
//	Nothing is replaced until everything has arrived and been checked. Files land in a
//	staging directory first and the local manifest is written last, so an update killed
//	halfway leaves the previous data readable and the next run redoes the difference.
//
//	Every file is checked against the hash the manifest gives for it, so a truncated
//	transfer that a proxy reported as success does not reach the cache.
//
//	A transient failure is retried with a doubling backoff, and a permanent one is not.
//
// Reading is somebody else's job. This produces a directory, and the generated tables
// read it.

package tabbit

import (
	"context"
	"crypto/md5"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// UpdateOptions is what an update is allowed to do. The zero value works: Update fills in
// every default.
type UpdateOptions struct {
	// ManifestFileName is the manifest's name at the base URL. The binary exporter writes
	// manifest-binary.json; the JSON exporter writes manifest-json.json.
	ManifestFileName string

	// MaxAttempts is how many times a request worth retrying is tried, the first attempt
	// included. Three by default, so two retries.
	MaxAttempts int

	// RetryDelay is how long to wait before the second attempt. Doubled after that.
	RetryDelay time.Duration

	// RequestTimeout bounds one request.
	RequestTimeout time.Duration

	// SkipHashCheck turns off checking a downloaded file against the manifest's hash.
	// There is no good reason to set it; the hash is already there and MD5 over a few
	// hundred kilobytes costs nothing next to the transfer that just happened.
	SkipHashCheck bool

	// Log receives progress and outcomes. Nil for none.
	Log func(string)
}

// UpdateResult is what an update did.
type UpdateResult struct {
	// UpToDate is true when the served manifest matched what was already here.
	UpToDate bool

	DownloadedCount int
	DownloadedBytes int64
	DeletedCount    int

	// LocalPath is the directory holding the data. Hand it to the generated tables'
	// ReadAll. Set even on failure, because the previous data is still there and still
	// readable - which is the point of failing the way this does.
	LocalPath string
}

// ManifestEntry is one file of the manifest, and the hash to check it by.
type ManifestEntry struct {
	Name string
	Size int64
	Hash string
}

// ParseManifest reads the entries out of a manifest's JSON.
func ParseManifest(data []byte) ([]ManifestEntry, error) {
	var manifest struct {
		Items []struct {
			Name string
			Size int64
			Hash string
		}
	}

	if err := json.Unmarshal(data, &manifest); err != nil {
		return nil, fmt.Errorf("the manifest could not be read: %w", err)
	}

	entries := make([]ManifestEntry, 0, len(manifest.Items))

	for _, item := range manifest.Items {
		if item.Name == "" {
			continue
		}

		entries = append(entries, ManifestEntry{Name: item.Name, Size: item.Size, Hash: item.Hash})
	}

	return entries, nil
}

// HashOf is the MD5 of some bytes, in the lower-case hex the manifest carries.
func HashOf(data []byte) string {
	sum := md5.Sum(data)
	return hex.EncodeToString(sum[:])
}

// Update brings cacheDirectory up to date with the data served under baseURL.
//
// The error is the reason the local copy is not current; the result is what happened
// either way, and its LocalPath still names readable data when the error is non-nil.
func Update(ctx context.Context, baseURL, cacheDirectory string, options UpdateOptions) (UpdateResult, error) {
	options = withDefaults(options)
	result := UpdateResult{LocalPath: cacheDirectory}

	manifestText, err := download(ctx, joinURL(baseURL, options.ManifestFileName), options)
	if err != nil {
		return result, err
	}

	remote, err := ParseManifest(manifestText)
	if err != nil {
		return result, err
	}

	local := readLocalManifest(filepath.Join(cacheDirectory, options.ManifestFileName))

	var wanted []ManifestEntry

	for _, entry := range remote {
		if !isCurrent(local, entry, cacheDirectory) {
			wanted = append(wanted, entry)
		}
	}

	var gone []string

	for _, entry := range local {
		if !contains(remote, entry.Name) {
			gone = append(gone, entry.Name)
		}
	}

	if len(wanted) == 0 && len(gone) == 0 {
		options.Log("tabbit: already up to date.")

		result.UpToDate = true
		return result, nil
	}

	options.Log(fmt.Sprintf("tabbit: %d file(s) to fetch, %d to remove.", len(wanted), len(gone)))

	// Everything lands here first. Nothing the caller can read is touched until the last
	// file has arrived and been checked.
	staging := filepath.Join(cacheDirectory, ".staging")

	if err := os.MkdirAll(cacheDirectory, 0o755); err != nil {
		return result, err
	}

	if err := os.RemoveAll(staging); err != nil {
		return result, err
	}

	if err := os.MkdirAll(staging, 0o755); err != nil {
		return result, err
	}

	for _, entry := range wanted {
		data, err := download(ctx, joinURL(baseURL, entry.Name), options)
		if err != nil {
			return result, err
		}

		if !options.SkipHashCheck && entry.Hash != "" {
			if actual := HashOf(data); !strings.EqualFold(actual, entry.Hash) {
				return result, fmt.Errorf(
					"%q arrived with hash %s, and the manifest says %s: nothing was replaced",
					entry.Name, actual, entry.Hash)
			}
		}

		staged := filepath.Join(staging, entry.Name)

		if err := os.MkdirAll(filepath.Dir(staged), 0o755); err != nil {
			return result, err
		}

		if err := os.WriteFile(staged, data, 0o644); err != nil {
			return result, err
		}

		result.DownloadedBytes += int64(len(data))
	}

	// From here on the update is applied. Nothing below reaches the network.
	for _, name := range gone {
		if err := os.Remove(filepath.Join(cacheDirectory, name)); err != nil && !os.IsNotExist(err) {
			return result, err
		}

		result.DeletedCount++
	}

	for _, entry := range wanted {
		target := filepath.Join(cacheDirectory, entry.Name)

		if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
			return result, err
		}

		if err := os.Rename(filepath.Join(staging, entry.Name), target); err != nil {
			return result, err
		}

		result.DownloadedCount++
	}

	// Last, and that ordering is the recovery story: a run killed before this point leaves
	// a manifest describing the data that is still on disk, so the next run fetches the
	// same files again rather than believing it has them.
	if err := os.WriteFile(filepath.Join(cacheDirectory, options.ManifestFileName), manifestText, 0o644); err != nil {
		return result, err
	}

	if err := os.RemoveAll(staging); err != nil {
		return result, err
	}

	options.Log(fmt.Sprintf("tabbit: updated. %d fetched, %d removed.",
		result.DownloadedCount, result.DeletedCount))

	return result, nil
}

func withDefaults(options UpdateOptions) UpdateOptions {
	if options.ManifestFileName == "" {
		options.ManifestFileName = "manifest-binary.json"
	}

	if options.MaxAttempts < 1 {
		options.MaxAttempts = 3
	}

	if options.RetryDelay <= 0 {
		options.RetryDelay = 500 * time.Millisecond
	}

	if options.RequestTimeout <= 0 {
		options.RequestTimeout = 30 * time.Second
	}

	if options.Log == nil {
		options.Log = func(string) {}
	}

	return options
}

// isCurrent reports whether the local copy already has this entry and the file to go with
// it. The file's presence is checked as well as the manifest's word for it: a cache
// somebody cleaned out by hand would otherwise never be refilled.
func isCurrent(local []ManifestEntry, entry ManifestEntry, cacheDirectory string) bool {
	for _, previous := range local {
		if previous.Name != entry.Name {
			continue
		}

		if previous.Hash != entry.Hash {
			return false
		}

		_, err := os.Stat(filepath.Join(cacheDirectory, entry.Name))
		return err == nil
	}

	return false
}

func contains(entries []ManifestEntry, name string) bool {
	for _, entry := range entries {
		if entry.Name == name {
			return true
		}
	}

	return false
}

// readLocalManifest reads the cached manifest. A missing or unreadable one is an empty
// manifest, which makes the next update fetch everything - the safe direction to be wrong
// in.
func readLocalManifest(filename string) []ManifestEntry {
	data, err := os.ReadFile(filename)
	if err != nil {
		return nil
	}

	entries, err := ParseManifest(data)
	if err != nil {
		return nil
	}

	return entries
}

// download fetches one URL, retrying what is worth retrying.
func download(ctx context.Context, url string, options UpdateOptions) ([]byte, error) {
	delay := options.RetryDelay

	for attempt := 1; ; attempt++ {
		data, err := fetch(ctx, url, options)

		if err == nil {
			return nil2empty(data), nil
		}

		var transient transientError

		if !asTransient(err, &transient) || attempt >= options.MaxAttempts {
			return nil, err
		}

		options.Log(fmt.Sprintf("tabbit: %v Retrying in %v (%d of %d).",
			err, delay, attempt, options.MaxAttempts))

		select {
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-time.After(delay):
		}

		// Doubling rather than a fixed wait: a server refusing because it is overloaded is
		// not helped by every client coming back at the same interval.
		delay *= 2
	}
}

func fetch(ctx context.Context, url string, options UpdateOptions) ([]byte, error) {
	timeout, cancel := context.WithTimeout(ctx, options.RequestTimeout)
	defer cancel()

	request, err := http.NewRequestWithContext(timeout, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}

	response, err := http.DefaultClient.Do(request)
	if err != nil {
		// The request never got an answer - DNS, a refused connection, a dropped link.
		// Worth another try.
		return nil, transientError{fmt.Errorf("%q could not be reached: %w", url, err)}
	}

	defer response.Body.Close()

	if response.StatusCode < 200 || response.StatusCode > 299 {
		err := fmt.Errorf("%q answered %s", url, response.Status)

		// 408 and 429 are the server asking for another attempt, and 5xx is it failing on
		// its own account. A 404 is an answer: retrying it costs three round trips to hear
		// the same thing.
		if isTransientStatus(response.StatusCode) {
			return nil, transientError{err}
		}

		return nil, err
	}

	body, err := io.ReadAll(response.Body)
	if err != nil {
		return nil, transientError{fmt.Errorf("%q was cut off: %w", url, err)}
	}

	return body, nil
}

func isTransientStatus(status int) bool {
	return status == 408 || status == 429 || (status >= 500 && status <= 599)
}

// transientError marks a failure the same request might survive a moment later.
type transientError struct {
	err error
}

func (e transientError) Error() string { return e.err.Error() }
func (e transientError) Unwrap() error { return e.err }

func asTransient(err error, out *transientError) bool {
	transient, ok := err.(transientError)

	if ok {
		*out = transient
	}

	return ok
}

func nil2empty(data []byte) []byte {
	if data == nil {
		return []byte{}
	}

	return data
}

// joinURL joins a base URL and a file name. Not filepath.Join, which on Windows produces a
// backslash and a URL no server will answer.
func joinURL(baseURL, name string) string {
	return strings.TrimRight(baseURL, "/") + "/" + strings.ReplaceAll(name, "\\", "/")
}
