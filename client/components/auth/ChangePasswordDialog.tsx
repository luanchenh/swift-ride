"use client";

import { FormEvent, useState } from "react";
import { Eye, EyeOff, KeyRound, LoaderCircle } from "lucide-react";
import { authService } from "@/service/AuthService";
import { useQuery } from "@/lib/use-query";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

export function ChangePasswordDialog() {
  const [open, setOpen] = useState(false);
  const [showPasswords, setShowPasswords] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState(false);
  const { execute, inProcess } = useQuery();

  function handleOpenChange(nextOpen: boolean) {
    setOpen(nextOpen);
    if (!nextOpen) {
      setError("");
      setSuccess(false);
      setShowPasswords(false);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setSuccess(false);

    const form = event.currentTarget;
    const formData = new FormData(form);
    const currentPassword = String(formData.get("currentPassword") ?? "");
    const newPassword = String(formData.get("newPassword") ?? "");
    const confirmPassword = String(formData.get("confirmPassword") ?? "");

    if (newPassword !== confirmPassword) {
      setError("Mật khẩu xác nhận không khớp.");
      return;
    }

    if (currentPassword === newPassword) {
      setError("Mật khẩu mới phải khác mật khẩu hiện tại.");
      return;
    }

    await execute(
      async () => {
        await authService.changePassword({ currentPassword, newPassword });
        form.reset();
        setSuccess(true);
      },
      { onError: (queryError) => setError(queryError.message) },
    );
  }

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="flex h-14 w-full items-center justify-center gap-2 rounded-xl border border-[#b8d5ca] bg-white text-base font-bold text-[#126b4f] transition-colors hover:bg-[#f4faf7] focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-[#126b4f]/15"
      >
        <KeyRound className="size-5" />
        Đổi mật khẩu
      </button>

      <Dialog open={open} onOpenChange={handleOpenChange}>
        <DialogContent className="p-5 sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="text-lg font-bold text-[#17211e]">
              Đổi mật khẩu
            </DialogTitle>
            <DialogDescription>
              Mật khẩu mới cần có ít nhất 6 ký tự.
            </DialogDescription>
          </DialogHeader>

          <form className="space-y-4" onSubmit={handleSubmit}>
            <PasswordField
              name="currentPassword"
              label="Mật khẩu hiện tại"
              autoComplete="current-password"
              visible={showPasswords}
            />
            <PasswordField
              name="newPassword"
              label="Mật khẩu mới"
              autoComplete="new-password"
              visible={showPasswords}
            />
            <PasswordField
              name="confirmPassword"
              label="Xác nhận mật khẩu mới"
              autoComplete="new-password"
              visible={showPasswords}
            />

            <label className="flex cursor-pointer items-center gap-2 text-sm text-[#55645e]">
              <input
                type="checkbox"
                checked={showPasswords}
                onChange={(event) => setShowPasswords(event.target.checked)}
                className="size-4 accent-[#126b4f]"
              />
              {showPasswords ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
              {showPasswords ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
            </label>

            {error && (
              <p role="alert" className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
                {error}
              </p>
            )}
            {success && (
              <p role="status" className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
                Đổi mật khẩu thành công.
              </p>
            )}

            <button
              type="submit"
              disabled={inProcess || success}
              className="flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-[#126b4f] px-4 font-bold text-white transition-colors hover:bg-[#0e5942] disabled:cursor-not-allowed disabled:opacity-60"
            >
              {inProcess && <LoaderCircle className="size-5 animate-spin" />}
              {inProcess ? "Đang cập nhật..." : success ? "Đã cập nhật" : "Cập nhật mật khẩu"}
            </button>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}

function PasswordField({
  name,
  label,
  autoComplete,
  visible,
}: {
  name: string;
  label: string;
  autoComplete: string;
  visible: boolean;
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm font-medium text-[#39453f]">{label}</span>
      <input
        name={name}
        type={visible ? "text" : "password"}
        autoComplete={autoComplete}
        minLength={6}
        required
        className="h-12 w-full rounded-xl border border-[#dce3e0] px-3 outline-none focus:border-[#126b4f] focus:ring-2 focus:ring-[#126b4f]/10"
      />
    </label>
  );
}
