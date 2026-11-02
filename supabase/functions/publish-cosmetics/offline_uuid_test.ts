import { assertEquals } from "jsr:@std/assert@1";
import { offlineUuid } from "./offline_uuid.ts";

Deno.test("Notch offline UUID matches Java", () => {
  assertEquals(offlineUuid("Notch"), "b50ad385-829d-3141-a216-7e7d7539ba7f");
});
