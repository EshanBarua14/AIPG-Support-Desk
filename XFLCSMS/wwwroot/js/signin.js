/* Xpert CSMS - pages before sign-in.
   The picture story on the left is animated in css/signin.css; this file only gives it a pause / play button. */
(function () {
    'use strict';

    var side = document.querySelector('[data-story]');
    var button = side && side.querySelector('[data-story-toggle]');
    if (!button) return;

    button.addEventListener('click', function () {
        var paused = side.classList.toggle('is-paused');
        var label = paused ? 'Play the animation' : 'Pause the animation';
        button.setAttribute('aria-label', label);
        button.setAttribute('title', label);
    });
})();
