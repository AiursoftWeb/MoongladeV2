(function () {
    'use strict';

    function init(options) {
        const editor = options.editor;
        const editorNode = editor?.getDomNode();
        if (!editor || !editorNode || !options.uploadUrl) return;
        const queue = [];
        let active = 0;

        function replace(placeholder, value) {
            const model = editor.getModel();
            if (model) model.setValue(model.getValue().replace(placeholder, value));
        }

        async function upload(slot, attempt = 0) {
            const form = new FormData();
            form.append('file', slot.file, slot.fileName);
            try {
                const response = await fetch(options.uploadUrl, { method: 'POST', body: form });
                if (response.status === 429 && attempt < 5) {
                    const retryAfter = Number.parseInt(response.headers.get('Retry-After') || '0', 10);
                    await new Promise(resolve => setTimeout(resolve, retryAfter > 0 ? retryAfter * 1000 : 60000));
                    return upload(slot, attempt + 1);
                }
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const result = await response.json();
                replace(slot.placeholder, `![](${result.InternetPath})`);
            } catch (error) {
                replace(slot.placeholder, '');
                console.error('Markdown image upload failed.', error);
                options.onError?.(error);
            }
        }

        function pump() {
            while (active < 3 && queue.length) {
                active++;
                upload(queue.shift()).finally(() => {
                    active--;
                    pump();
                });
            }
        }

        function enqueue(files) {
            const slots = files.map(file => {
                const mimeExtension = (file.type.split('/')[1] || 'png').replace('jpeg', 'jpg').split('+')[0];
                const originalExtension = file.name.includes('.') ? file.name.split('.').pop() : '';
                const fileName = `paste-${crypto.randomUUID().replaceAll('-', '')}.${originalExtension || mimeExtension}`;
                return { file, fileName, placeholder: `![uploading...](${fileName})` };
            });
            if (!slots.length) return;
            const selection = editor.getSelection();
            const model = editor.getModel();
            const line = model.getLineContent(selection.startLineNumber);
            editor.executeEdits('markdown-image-upload', [{
                range: selection,
                text: (line.trim() && selection.startColumn > 1 ? '\n' : '') +
                    slots.map(slot => slot.placeholder).join('\n') + '\n',
                forceMoveMarkers: true
            }]);
            queue.push(...slots);
            pump();
        }

        document.addEventListener('paste', event => {
            if (!editor.hasTextFocus()) return;
            const files = Array.from(event.clipboardData?.items || [])
                .filter(item => item.kind === 'file' && item.type.startsWith('image/'))
                .map(item => item.getAsFile()).filter(Boolean);
            if (!files.length) return;
            event.preventDefault();
            event.stopPropagation();
            enqueue(files);
        }, true);

        editorNode.addEventListener('dragover', event => {
            if (Array.from(event.dataTransfer?.items || []).some(item => item.kind === 'file' && item.type.startsWith('image/'))) {
                event.preventDefault();
                event.stopPropagation();
            }
        }, true);
        editorNode.addEventListener('drop', event => {
            const files = Array.from(event.dataTransfer?.files || []).filter(file => file.type.startsWith('image/'));
            if (!files.length) return;
            event.preventDefault();
            event.stopPropagation();
            editor.focus();
            enqueue(files);
        }, true);
    }

    window.AiursoftMarkdownImageUpload = { init };
})();
