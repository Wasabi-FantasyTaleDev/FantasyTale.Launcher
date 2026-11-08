// Vue mounting precedes the asynchronous sprite/font renderer. Publish readiness
// only after that renderer attaches a surface, or report its actual failure.
export function waitForNativeSurface(root, timeoutMs = 15000) {
    let finish;
    const ready = new Promise((resolve, reject) => {
        let completed = false;
        const observer = new MutationObserver(check);
        const timer = setTimeout(() => finish(new Error('The game UI surface did not become ready.')), timeoutMs);
        finish = error => {
            if (completed) return;
            completed = true;
            observer.disconnect();
            clearTimeout(timer);
            if (error) reject(error); else resolve();
        };
        function check() {
            if (root.querySelector('.bf-native-host')?.firstElementChild) finish();
        }
        observer.observe(root, { childList: true, subtree: true });
        check();
    });
    return { ready, fail: error => finish(error instanceof Error ? error : new Error(String(error))) };
}
