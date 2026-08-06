document.addEventListener("click", (event) => {
    const toggle = event.target.closest("#login-password-toggle");
    if (!toggle) return;

    const passwordInput = document.getElementById("login-password");
    if (!passwordInput) return;

    const shouldShow = passwordInput.type === "password";
    passwordInput.type = shouldShow ? "text" : "password";

    const icon = toggle.querySelector("i");
    icon?.classList.toggle("bi-eye-fill", !shouldShow);
    icon?.classList.toggle("bi-eye-slash-fill", shouldShow);

    const actionLabel = shouldShow ? "پنهان کردن رمز عبور" : "نمایش رمز عبور";
    toggle.setAttribute("aria-label", actionLabel);
    toggle.setAttribute("title", actionLabel);
    toggle.setAttribute("aria-pressed", shouldShow.toString());
});
